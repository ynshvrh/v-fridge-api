using System.ComponentModel.DataAnnotations;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VFridge.Api.Auth;
using VFridge.Api.Configuration;
using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/signup", SignUpAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/verify-email", VerifyEmailAsync);
        group.MapPost("/verify-email", VerifyEmailJsonAsync);
        group.MapPost("/resend-verification", ResendVerificationAsync);
        group.MapPost("/google", GoogleSignInAsync);
        group.MapGet("/me", GetMeAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> SignUpAsync(SignUpRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var (ok, error, user) = await auth.SignUpAsync(req, ct);
        return ok
            ? Results.Created("/auth/me", new
            {
                user,
                message = "Account created. Check your inbox — we sent a confirmation email."
            })
            : Results.BadRequest(new { error });
    }

    private static async Task<IResult> LoginAsync(LoginRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var (ok, code, pair) = await auth.LoginAsync(req, ct);
        if (ok) return Results.Ok(pair);

        return code switch
        {
            AuthService.LoginErrorEmailNotVerified => Results.Json(
                new { code, error = "Email is not verified yet. Check your inbox or request a new email." },
                statusCode: StatusCodes.Status403Forbidden),
            _ => Results.Json(new { code, error = "Invalid email or password" },
                statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> RefreshAsync(RefreshRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var (ok, error, pair) = await auth.RefreshAsync(req.RefreshToken, ct);
        return ok ? Results.Ok(pair) : Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LogoutAsync(LogoutRequest req, AuthService auth, CancellationToken ct)
    {
        await auth.RevokeAsync(req.RefreshToken, ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> VerifyEmailAsync(
        [FromQuery] string token,
        AuthService auth,
        IOptions<FrontendOptions> frontend,
        CancellationToken ct)
    {
        // Forward the raw token to the SPA — it'll call POST /auth/verify-email which both
        // marks the email verified and returns a token pair so the user is auto-logged in.
        // This avoids double-consuming the one-shot token and keeps tokens out of the URL
        // visible in browser history (the SPA cleans up after success).
        var baseUrl = frontend.Value.BaseUrl.TrimEnd('/');
        return Results.Redirect($"{baseUrl}/verify-email?token={Uri.EscapeDataString(token)}");
    }

    private sealed record VerifyEmailRequest(string Token);

    private static async Task<IResult> VerifyEmailJsonAsync(
        VerifyEmailRequest req,
        AuthService auth,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return Results.BadRequest(new { error = "Token is required" });

        var (ok, error, userId) = await auth.VerifyEmailAsync(req.Token, ct);
        if (!ok || userId is null) return Results.BadRequest(new { error });

        var pair = await auth.IssueTokensForUserAsync(userId.Value, ct);
        return pair is null
            ? Results.Problem("Failed to issue tokens", statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok(pair);
    }

    private static async Task<IResult> ResendVerificationAsync(ResendVerificationRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        await auth.ResendVerificationAsync(req.Email, ct);
        // Always 200 to avoid revealing account existence
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GoogleSignInAsync(
        GoogleCallbackRequest req,
        AuthService auth,
        IOptions<Configuration.GoogleOptions> google,
        CancellationToken ct)
    {
        var clientId = google.Value.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            return Results.Problem("Google OAuth is not configured", statusCode: StatusCodes.Status503ServiceUnavailable);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
        }
        catch (InvalidJwtException)
        {
            return Results.Json(new { error = "Invalid Google token" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || payload.EmailVerified != true)
            return Results.BadRequest(new { error = "Google did not confirm the email" });

        var pair = await auth.SignInWithGoogleAsync(payload.Subject, payload.Email, payload.Name, ct);
        return Results.Ok(pair);
    }

    private static async Task<IResult> GetMeAsync(AuthService auth, ICurrentUser me, CancellationToken ct)
    {
        if (me.UserId is not int uid) return Results.Unauthorized();
        var user = await auth.GetCurrentUserAsync(uid, ct);
        return user is null ? Results.NotFound() : Results.Ok(user);
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
