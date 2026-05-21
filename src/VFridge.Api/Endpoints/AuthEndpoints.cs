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

        group.MapPost("/signup", SignUpAsync)
            .WithName("Signup")
            .WithSummary("Create a new account")
            .WithDescription("Creates a user, sends a verification email, and returns the user summary. The account is unusable until the email is verified.")
            .Produces(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .ProducesValidationProblem();

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Email + password sign-in")
            .WithDescription("Returns a TokenPair on success. Returns 403 EMAIL_NOT_VERIFIED if the account exists but the email has not been confirmed yet, or 401 BAD_CREDENTIALS otherwise.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status401Unauthorized)
            .Produces<ApiError>(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithSummary("Rotate the refresh token")
            .WithDescription("Exchanges the supplied refresh token for a fresh pair. The presented refresh token is revoked atomically.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status401Unauthorized)
            .ProducesValidationProblem();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revoke a refresh token")
            .WithDescription("Best-effort: returns 200 regardless of whether the token was active.")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/verify-email", VerifyEmailAsync)
            .WithName("VerifyEmailRedirect")
            .WithSummary("Email-link landing endpoint")
            .WithDescription("Redirects the verification link from the email straight to the SPA's /verify-email page so the SPA can exchange the token via POST. Avoids consuming the one-shot token from a non-browser preview.")
            .Produces(StatusCodes.Status302Found);

        group.MapPost("/verify-email", VerifyEmailJsonAsync)
            .WithName("VerifyEmail")
            .WithSummary("Exchange a verification token for a session")
            .WithDescription("On success, marks the email verified and returns a TokenPair (auto-login). Error codes: TOKEN_MISSING, TOKEN_NOT_FOUND, TOKEN_USED, TOKEN_EXPIRED.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-verification", ResendVerificationAsync)
            .WithName("ResendVerification")
            .WithSummary("Resend the verification email")
            .WithDescription("Always returns 200 to avoid leaking whether an account with the given email exists.")
            .Produces(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        group.MapPost("/google", GoogleSignInAsync)
            .WithName("GoogleSignIn")
            .WithSummary("Sign in with a Google ID token")
            .WithDescription("Validates the ID token against the configured client ID, then issues a TokenPair. Creates the user on first sign-in.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest)
            .Produces<ApiError>(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithName("GetMe")
            .WithSummary("Current authenticated user")
            .Produces<UserSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> SignUpAsync(SignUpRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var (ok, code, user) = await auth.SignUpAsync(req, ct);
        if (ok)
        {
            return Results.Created("/auth/me", new
            {
                user,
                message = "Account created. Check your inbox — we sent a confirmation email."
            });
        }

        var error = code switch
        {
            AuthService.SignUpErrorEmailExists => "A user with this email already exists",
            _ => "Sign-up failed"
        };
        return Results.BadRequest(new { code, error });
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

        var (ok, code, pair) = await auth.RefreshAsync(req.RefreshToken, ct);
        if (ok) return Results.Ok(pair);

        var error = code switch
        {
            AuthService.RefreshErrorInvalid => "Refresh token is invalid",
            _ => "Refresh failed"
        };
        return Results.Json(new { code, error }, statusCode: StatusCodes.Status401Unauthorized);
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
            return Results.BadRequest(new { code = "TOKEN_MISSING", error = "Token is required" });

        var (ok, code, userId) = await auth.VerifyEmailAsync(req.Token, ct);
        if (!ok || userId is null)
        {
            var error = code switch
            {
                AuthService.VerifyErrorTokenNotFound => "Verification token not found",
                AuthService.VerifyErrorTokenUsed => "Token has already been used",
                AuthService.VerifyErrorTokenExpired => "Token has expired",
                _ => "Verification failed"
            };
            return Results.BadRequest(new { code, error });
        }

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
            return Results.Json(
                new { code = "GOOGLE_TOKEN_INVALID", error = "Invalid Google token" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || payload.EmailVerified != true)
            return Results.BadRequest(new { code = "GOOGLE_EMAIL_UNVERIFIED", error = "Google did not confirm the email" });

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
