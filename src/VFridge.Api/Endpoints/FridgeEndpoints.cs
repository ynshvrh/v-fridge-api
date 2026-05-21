using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VFridge.Api.Auth;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class FridgeEndpoints
{
    private static readonly TimeSpan InviteWindow = TimeSpan.FromDays(7);

    public static IEndpointRouteBuilder MapFridgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/fridges").WithTags("Fridges");

        group.MapGet("/", ListAsync)
            .WithName("ListFridges")
            .WithSummary("List fridges the caller is a member of")
            .Produces<List<FridgeResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/", CreateAsync)
            .WithName("CreateFridge")
            .WithSummary("Create a new fridge the caller will own")
            .Produces<FridgeResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPatch("/{id:int}", RenameAsync)
            .WithName("RenameFridge")
            .WithSummary("Rename an owned fridge")
            .Produces<FridgeResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteFridge")
            .WithSummary("Delete a fridge (owner only). Cascades products / members / invites.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .Produces<ApiError>(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:int}/members/me", LeaveAsync)
            .WithName("LeaveFridge")
            .WithSummary("Leave a fridge. Owners cannot leave their own fridge — delete it instead.")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:int}/invites", CreateInviteAsync)
            .WithName("CreateFridgeInvite")
            .WithSummary("Invite someone by email to join an owned fridge")
            .Produces<InviteResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapPost("/accept", AcceptInviteAsync)
            .WithName("AcceptFridgeInvite")
            .WithSummary("Accept a fridge invite token (requires a verified account)")
            .Produces<AcceptInviteResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> ListAsync(VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var items = await db.FridgeMembers
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

    private static async Task<IResult> CreateAsync(
        CreateFridgeRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = new Fridge { Name = req.Name.Trim(), OwnerId = uid };
        db.Fridges.Add(fridge);
        await db.SaveChangesAsync(ct);

        db.FridgeMembers.Add(new FridgeMember
        {
            FridgeId = fridge.Id,
            UserId = uid,
            Role = FridgeRoles.Owner
        });
        await db.SaveChangesAsync(ct);

        var resp = new FridgeResponse(fridge.Id, fridge.Name, fridge.OwnerId, FridgeRoles.Owner, 1, fridge.CreatedAt);
        return Results.Created($"/fridges/{fridge.Id}", resp);
    }

    private static async Task<IResult> RenameAsync(
        int id,
        RenameFridgeRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = await db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can rename a fridge" },
                statusCode: StatusCodes.Status403Forbidden);

        fridge.Name = req.Name.Trim();
        await db.SaveChangesAsync(ct);

        var members = await db.FridgeMembers.CountAsync(m => m.FridgeId == fridge.Id, ct);
        return Results.Ok(new FridgeResponse(fridge.Id, fridge.Name, fridge.OwnerId, FridgeRoles.Owner, members, fridge.CreatedAt));
    }

    private static async Task<IResult> DeleteAsync(int id, VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var fridge = await db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can delete a fridge" },
                statusCode: StatusCodes.Status403Forbidden);

        // Don't let a user delete their LAST owned fridge — they'd have nowhere to put products.
        var owned = await db.Fridges.CountAsync(f => f.OwnerId == uid, ct);
        if (owned <= 1)
        {
            return Results.Json(new { code = "LAST_FRIDGE", error = "Cannot delete your only fridge" },
                statusCode: StatusCodes.Status409Conflict);
        }

        db.Fridges.Remove(fridge);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> LeaveAsync(int id, VFridgeDbContext db, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();

        var member = await db.FridgeMembers
            .Include(m => m.Fridge)
            .FirstOrDefaultAsync(m => m.FridgeId == id && m.UserId == uid, ct);
        if (member is null) return Results.NotFound(new { code = "NOT_A_MEMBER", error = "You are not a member of this fridge" });
        if (member.Fridge.OwnerId == uid)
            return Results.Json(new { code = "OWNER_CANNOT_LEAVE", error = "Owners cannot leave their own fridge — delete it instead" },
                statusCode: StatusCodes.Status403Forbidden);

        db.FridgeMembers.Remove(member);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> CreateInviteAsync(
        int id,
        InviteFridgeMemberRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        ITokenService tokens,
        IEmailSender email,
        IOptions<FrontendOptions> frontend,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var fridge = await db.Fridges.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fridge is null) return Results.NotFound(new { code = "FRIDGE_NOT_FOUND", error = "Fridge not found" });
        if (fridge.OwnerId != uid)
            return Results.Json(new { code = "NOT_FRIDGE_OWNER", error = "Only the owner can invite members" },
                statusCode: StatusCodes.Status403Forbidden);

        var raw = tokens.GenerateRefreshToken();
        var invite = new FridgeInvite
        {
            FridgeId = fridge.Id,
            Email = req.Email.Trim().ToLowerInvariant(),
            TokenHash = tokens.Hash(raw),
            ExpiresAt = DateTime.UtcNow.Add(InviteWindow)
        };
        db.FridgeInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        var url = $"{frontend.Value.BaseUrl.TrimEnd('/')}/invite?token={Uri.EscapeDataString(raw)}";
        var html = $"""
            <div style="font-family: system-ui, sans-serif; max-width:480px; margin:auto;">
              <h2 style="color:#8C5383;">Join "{System.Net.WebUtility.HtmlEncode(fridge.Name)}" on V-Fridge</h2>
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
            await email.SendAsync(invite.Email, "V-Fridge — you have an invite", html, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send fridge invite to {Email}", invite.Email);
        }

        return Results.Created($"/fridges/{fridge.Id}/invites/{invite.Id}",
            new InviteResponse(invite.Id, invite.Email, invite.ExpiresAt, invite.AcceptedAt));
    }

    private static async Task<IResult> AcceptInviteAsync(
        AcceptInviteRequest req,
        VFridgeDbContext db,
        ICurrentUser me,
        ITokenService tokens,
        CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var verified = await db.EmailVerifications.AnyAsync(v => v.UserId == uid, ct);
        if (!verified)
            return Results.Json(new { code = "EMAIL_NOT_VERIFIED", error = "Verify your email before joining a fridge" },
                statusCode: StatusCodes.Status400BadRequest);

        var hash = tokens.Hash(req.Token);
        var invite = await db.FridgeInvites
            .Include(i => i.Fridge)
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        if (invite is null) return Results.BadRequest(new { code = "INVITE_NOT_FOUND", error = "Invite not found" });
        if (invite.AcceptedAt is not null) return Results.BadRequest(new { code = "INVITE_USED", error = "Invite already accepted" });
        if (invite.ExpiresAt < DateTime.UtcNow) return Results.BadRequest(new { code = "INVITE_EXPIRED", error = "Invite has expired" });

        var alreadyMember = await db.FridgeMembers.AnyAsync(m => m.FridgeId == invite.FridgeId && m.UserId == uid, ct);
        if (!alreadyMember)
        {
            db.FridgeMembers.Add(new FridgeMember
            {
                FridgeId = invite.FridgeId,
                UserId = uid,
                Role = FridgeRoles.Member
            });
        }
        invite.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

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
