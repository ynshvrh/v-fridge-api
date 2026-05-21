using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;

namespace VFridge.Api.Services;

public sealed class AuthService(
    VFridgeDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IEmailSender email,
    IOptions<JwtOptions> jwtOpts,
    IOptions<FrontendOptions> frontendOpts,
    ILogger<AuthService> logger)
{
    private readonly JwtOptions _jwt = jwtOpts.Value;
    private readonly FrontendOptions _frontend = frontendOpts.Value;

    public const string SignUpErrorEmailExists = "EMAIL_EXISTS";

    public async Task<(bool Ok, string? ErrorCode, UserSummary? User)> SignUpAsync(SignUpRequest req, CancellationToken ct)
    {
        var emailNormalized = req.Email.Trim().ToLowerInvariant();
        var exists = await db.Users.AnyAsync(u => u.Email == emailNormalized, ct);
        if (exists) return (false, SignUpErrorEmailExists, null);

        var user = new User
        {
            Email = emailNormalized,
            Username = req.Username.Trim(),
            Password = hasher.Hash(req.Password)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        await EnsurePersonalFridgeAsync(user, ct);

        var summary = new UserSummary(user.Id, user.Username, user.Email, EmailVerified: false);
        await SendVerificationEmailAsync(user, ct);
        return (true, null, summary);
    }

    private async Task EnsurePersonalFridgeAsync(User user, CancellationToken ct)
    {
        var hasFridge = await db.Fridges.AnyAsync(f => f.OwnerId == user.Id, ct);
        if (hasFridge) return;

        var fridge = new Data.Entities.Fridge
        {
            Name = $"{user.Username}'s fridge",
            OwnerId = user.Id
        };
        db.Fridges.Add(fridge);
        await db.SaveChangesAsync(ct);

        db.FridgeMembers.Add(new Data.Entities.FridgeMember
        {
            FridgeId = fridge.Id,
            UserId = user.Id,
            Role = Data.Entities.FridgeRoles.Owner
        });
        await db.SaveChangesAsync(ct);
    }

    public const string LoginErrorBadCredentials = "BAD_CREDENTIALS";
    public const string LoginErrorEmailNotVerified = "EMAIL_NOT_VERIFIED";

    public async Task<(bool Ok, string? ErrorCode, TokenPair? Tokens)> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var emailNormalized = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized, ct);
        if (user is null || !hasher.Verify(req.Password, user.Password))
            return (false, LoginErrorBadCredentials, null);

        var emailVerified = await db.EmailVerifications.AnyAsync(v => v.UserId == user.Id, ct);
        if (!emailVerified) return (false, LoginErrorEmailNotVerified, null);

        var pair = await IssueTokenPairAsync(user, ct);
        return (true, null, pair);
    }

    public const string RefreshErrorInvalid = "REFRESH_INVALID";

    public async Task<(bool Ok, string? ErrorCode, TokenPair? Tokens)> RefreshAsync(string rawRefreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(rawRefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive)
            return (false, RefreshErrorInvalid, null);

        // Rotate: revoke the old token immediately, issue a fresh pair.
        stored.RevokedAt = DateTime.UtcNow;

        var pair = await IssueTokenPairAsync(stored.User, ct);
        return (true, null, pair);
    }

    public async Task RevokeAsync(string rawRefreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(rawRefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAt is not null) return;

        stored.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public const string VerifyErrorTokenNotFound = "TOKEN_NOT_FOUND";
    public const string VerifyErrorTokenUsed = "TOKEN_USED";
    public const string VerifyErrorTokenExpired = "TOKEN_EXPIRED";

    public async Task<(bool Ok, string? ErrorCode, int? UserId)> VerifyEmailAsync(string rawToken, CancellationToken ct)
    {
        var hash = tokens.Hash(rawToken);
        var record = await db.EmailVerificationTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (record is null) return (false, VerifyErrorTokenNotFound, null);
        if (record.UsedAt is not null) return (false, VerifyErrorTokenUsed, null);
        if (record.ExpiresAt < DateTime.UtcNow) return (false, VerifyErrorTokenExpired, null);

        record.UsedAt = DateTime.UtcNow;

        var existing = await db.EmailVerifications.FindAsync([record.UserId], ct);
        if (existing is null)
        {
            db.EmailVerifications.Add(new EmailVerification
            {
                UserId = record.UserId,
                VerifiedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return (true, null, record.UserId);
    }

    /// <summary>Issues a fresh token pair for a known-good user id — used right after email verification.</summary>
    public async Task<TokenPair?> IssueTokensForUserAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        return user is null ? null : await IssueTokenPairAsync(user, ct);
    }

    public async Task ResendVerificationAsync(string email, CancellationToken ct)
    {
        var emailNormalized = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized, ct);
        if (user is null) return; // intentionally silent — don't reveal account existence

        var verified = await db.EmailVerifications.AnyAsync(v => v.UserId == user.Id, ct);
        if (verified) return;

        await SendVerificationEmailAsync(user, ct);
    }

    /// <summary>Find or create a user from a Google account and issue tokens.</summary>
    public async Task<TokenPair> SignInWithGoogleAsync(string googleSub, string email, string? name, CancellationToken ct)
    {
        var emailNormalized = email.Trim().ToLowerInvariant();

        var existingOAuth = await db.OAuthLogins
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Provider == "google" && o.ProviderUserId == googleSub, ct);

        User user;
        if (existingOAuth is not null)
        {
            user = existingOAuth.User;
        }
        else
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailNormalized, ct)
                   ?? new User
                   {
                       Email = emailNormalized,
                       Username = string.IsNullOrWhiteSpace(name) ? emailNormalized.Split('@')[0] : name,
                       // Random placeholder hash; password-less Google users won't ever pass Verify.
                       Password = hasher.Hash(Guid.NewGuid().ToString("N"))
                   };

            if (user.Id == 0) { db.Users.Add(user); await db.SaveChangesAsync(ct); }

            db.OAuthLogins.Add(new OAuthLogin
            {
                UserId = user.Id,
                Provider = "google",
                ProviderUserId = googleSub
            });

            // Google emails are pre-verified.
            if (!await db.EmailVerifications.AnyAsync(v => v.UserId == user.Id, ct))
            {
                db.EmailVerifications.Add(new EmailVerification
                {
                    UserId = user.Id,
                    VerifiedAt = DateTime.UtcNow
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await EnsurePersonalFridgeAsync(user, ct);
        return await IssueTokenPairAsync(user, ct);
    }

    public async Task<UserSummary?> GetCurrentUserAsync(int userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;
        var verified = await db.EmailVerifications.AnyAsync(v => v.UserId == userId, ct);
        return new UserSummary(user.Id, user.Username, user.Email, verified);
    }

    private async Task<TokenPair> IssueTokenPairAsync(User user, CancellationToken ct)
    {
        var (access, accessExpires) = tokens.IssueAccessToken(user.Id, user.Username, user.Email);

        var raw = tokens.GenerateRefreshToken();
        var hash = tokens.Hash(raw);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = refreshExpires
        });
        await db.SaveChangesAsync(ct);

        var verified = await db.EmailVerifications.AnyAsync(v => v.UserId == user.Id, ct);
        return new TokenPair(
            access,
            accessExpires,
            raw,
            refreshExpires,
            new UserSummary(user.Id, user.Username, user.Email, verified));
    }

    private async Task SendVerificationEmailAsync(User user, CancellationToken ct)
    {
        var raw = tokens.GenerateRefreshToken(); // reusing the 256-bit url-safe generator
        var hash = tokens.Hash(raw);

        db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        });
        await db.SaveChangesAsync(ct);

        var verifyUrl = $"{_frontend.BaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(raw)}";

        var html = $"""
            <div style="font-family: system-ui, sans-serif; max-width:480px; margin:auto;">
              <h2 style="color:#8C5383;">Welcome to V-Fridge!</h2>
              <p>Hi <strong>{user.Username}</strong>, thanks for signing up.</p>
              <p>Please confirm your email address by clicking the button below:</p>
              <p>
                <a href="{verifyUrl}"
                   style="display:inline-block;background:#8C5383;color:#fff;padding:12px 24px;
                          border-radius:12px;text-decoration:none;font-weight:600;">
                  Verify email
                </a>
              </p>
              <p style="color:#666;font-size:13px;">If the button does not work, copy and paste this link:<br>{verifyUrl}</p>
              <p style="color:#999;font-size:12px;">This link is valid for 24 hours.</p>
            </div>
            """;

        try
        {
            await email.SendAsync(user.Email, "V-Fridge — please confirm your email", html, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
        }
    }
}
