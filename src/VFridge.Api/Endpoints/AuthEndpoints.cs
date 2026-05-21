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
                message = "Акаунт створено. Перевірте пошту — ми надіслали лист для підтвердження."
            })
            : Results.BadRequest(new { error });
    }

    private static async Task<IResult> LoginAsync(LoginRequest req, AuthService auth, CancellationToken ct)
    {
        if (!TryValidate(req, out var errors)) return Results.ValidationProblem(errors);

        var (ok, error, pair) = await auth.LoginAsync(req, ct);
        return ok ? Results.Ok(pair) : Results.Json(new { error }, statusCode: StatusCodes.Status401Unauthorized);
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
        var (ok, error) = await auth.VerifyEmailAsync(token, ct);

        var baseUrl = frontend.Value.BaseUrl.TrimEnd('/');
        var redirect = ok
            ? $"{baseUrl}/signin?verified=1"
            : $"{baseUrl}/signin?verified=0&reason={Uri.EscapeDataString(error ?? "")}";
        return Results.Redirect(redirect);
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
            return Results.Problem("Google OAuth не налаштовано", statusCode: StatusCodes.Status503ServiceUnavailable);

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(req.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
        }
        catch (InvalidJwtException)
        {
            return Results.Json(new { error = "Невалідний Google токен" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(payload.Email) || payload.EmailVerified != true)
            return Results.BadRequest(new { error = "Google не підтвердив email" });

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
