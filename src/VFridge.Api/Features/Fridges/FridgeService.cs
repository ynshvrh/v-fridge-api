using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VFridge.Api.Auth;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.Fridges;

public class FridgeService : IFridgeService
{
    private static readonly TimeSpan InviteWindow = TimeSpan.FromDays(7);

    private readonly VFridgeDbContext _db;
    private readonly ICurrentUser _me;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;
    private readonly IOptions<FrontendOptions> _frontend;
    private readonly ILogger<FridgeService> _logger;

    public FridgeService(
        VFridgeDbContext db,
        ICurrentUser me,
        ITokenService tokens,
        IEmailSender email,
        IOptions<FrontendOptions> frontend,
        ILogger<FridgeService> logger)
    {
        _db = db;
        _me = me;
        _tokens = tokens;
        _email = email;
        _frontend = frontend;
        _logger = logger;
    }

    public async Task<IResult> ListAsync(CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var items = await _db.FridgeMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.FridgeId)
            .Select(m => new FridgeResponse(
                m.Fridge.Id,
                m.Fridge.Name,
                m.Fridge.OwnerId,
                m.Role,
                m.Fridge.Members.Count,
                m.Fridge.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(items);
    }

    public async Task<IResult> CreateAsync(CreateFridgeRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = new Fridge { Name = req.Name.Trim(), OwnerId = uid };
        _db.Fridges.Add(fridge);
        await _db.SaveChangesAsync(ct);

        _db.FridgeMembers.Add(new FridgeMember
        {
            FridgeId = fridge.Id,
            UserId = uid,
            Role = FridgeRoles.Owner
        });
        await _db.SaveChangesAsync(ct);

        var resp = new FridgeResponse(fridge.Id, fridge.Name, fridge.OwnerId, FridgeRoles.Owner, 1, fridge.CreatedAt);
        return Results.Created($"/fridges/{fridge.Id}", resp);
    }

    public async Task<IResult> RenameAsync(int id, RenameFridgeRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = await _db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can rename a fridge" },
                statusCode: StatusCodes.Status403Forbidden);

        fridge.Name = req.Name.Trim();
        await _db.SaveChangesAsync(ct);

        var members = await _db.FridgeMembers.CountAsync(m => m.FridgeId == fridge.Id, ct);
        return Results.Ok(new FridgeResponse(fridge.Id, fridge.Name, fridge.OwnerId, FridgeRoles.Owner, members, fridge.CreatedAt));
    }

    public async Task<IResult> DeleteAsync(int id, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var fridge = await _db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can delete a fridge" },
                statusCode: StatusCodes.Status403Forbidden);

        _db.Fridges.Remove(fridge);
        await _db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    public async Task<IResult> LeaveAsync(int id, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();

        var member = await _db.FridgeMembers
            .Include(m => m.Fridge)
            .FirstOrDefaultAsync(m => m.FridgeId == id && m.UserId == uid, ct);
        if (member is null) return Results.NotFound(new { code = "NOT_A_MEMBER", error = "You are not a member of this fridge" });
        if (member.Fridge.OwnerId == uid)
            return Results.Json(new { code = "OWNER_CANNOT_LEAVE", error = "Owners cannot leave their own fridge — delete it instead" },
                statusCode: StatusCodes.Status403Forbidden);

        _db.FridgeMembers.Remove(member);
        await _db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    public async Task<IResult> CreateInviteAsync(int id, InviteFridgeMemberRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = await _db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can invite members" },
                statusCode: StatusCodes.Status403Forbidden);

        var targetEmail = req.Email.Trim().ToLowerInvariant();
        var existingInvite = await _db.FridgeInvites.FirstOrDefaultAsync(i =>
            i.FridgeId == fridge.Id &&
            i.Email == targetEmail &&
            i.AcceptedAt == null &&
            i.ExpiresAt > DateTime.UtcNow, ct);
        if (existingInvite is not null)
        {
            return Results.BadRequest(new { code = "INVITE_ALREADY_PENDING", error = "An active invite for this email address already exists" });
        }

        var raw = _tokens.GenerateRefreshToken();
        var invite = new FridgeInvite
        {
            FridgeId = fridge.Id,
            Email = targetEmail,
            TokenHash = _tokens.Hash(raw),
            ExpiresAt = DateTime.UtcNow.Add(InviteWindow)
        };
        _db.FridgeInvites.Add(invite);
        await _db.SaveChangesAsync(ct);

        var url = $"{_frontend.Value.BaseUrl.TrimEnd('/')}/invite?token={Uri.EscapeDataString(raw)}";
        var html = $"""
            <div style="font-family: system-ui, sans-serif; max-width:480px; margin:auto;">
              <h2 style="color:#8C5383;">Join "{WebUtility.HtmlEncode(fridge.Name)}" on V-Fridge</h2>
              <p>You have been invited to share a fridge on V-Fridge. The invite is valid for 7 days.</p>
              <p>
                <a href="{url}"
                   style="display:inline-block;background:#8C5383;color:#fff;padding:12px 24px;
                          border-radius:12px;text-decoration:none;font-weight:600;">
                  Accept invite
                </a>
              </p>
              <p style="color:#666;font-size:13px;">Or paste this link into your browser:<br>{url}</p>
            </div>
            """;

        try
        {
            await _email.SendAsync(invite.Email, "V-Fridge — you have an invite", html, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send fridge invite to {Email}", invite.Email);
        }

        return Results.Created($"/fridges/{fridge.Id}/invites/{invite.Id}",
            new InviteResponse(invite.Id, invite.Email, invite.ExpiresAt, invite.AcceptedAt));
    }

    public async Task<IResult> AcceptInviteAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        if (_me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
        if (user is null) return Results.Unauthorized();

        var verified = await _db.EmailVerifications.AnyAsync(v => v.UserId == uid, ct);
        if (!verified)
            return Results.Json(new { code = "EMAIL_NOT_VERIFIED", error = "Verify your email before joining a fridge" },
                statusCode: StatusCodes.Status400BadRequest);

        var hash = _tokens.Hash(req.Token);
        var invite = await _db.FridgeInvites
            .Include(i => i.Fridge)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        if (invite is null) return Results.BadRequest(new { code = "INVITE_NOT_FOUND", error = "Invite not found" });
        if (invite.AcceptedAt is not null) return Results.BadRequest(new { code = "INVITE_USED", error = "Invite already accepted" });
        if (invite.ExpiresAt < DateTime.UtcNow) return Results.BadRequest(new { code = "INVITE_EXPIRED", error = "Invite has expired" });

        if (!string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { code = "INVITE_EMAIL_MISMATCH", error = "This invite was issued to a different email address" });
        }

        var alreadyMember = await _db.FridgeMembers.AnyAsync(m => m.FridgeId == invite.FridgeId && m.UserId == uid, ct);
        if (!alreadyMember)
        {
            _db.FridgeMembers.Add(new FridgeMember
            {
                FridgeId = invite.FridgeId,
                UserId = uid,
                Role = FridgeRoles.Member
            });
        }
        invite.AcceptedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Results.Ok(new AcceptInviteResponse(invite.FridgeId, invite.Fridge.Name));
    }

    private static bool TryValidate<T>(T instance, out Dictionary<string, string[]> errors) where T : class
    {
        var ctx = new ValidationContext(instance);
        var results = new List<ValidationResult>();
        var ok = Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);
        errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(""), (r, m) => (m, r.ErrorMessage ?? "Invalid"))
            .GroupBy(t => t.m)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Item2).ToArray());
        return ok;
    }
}
