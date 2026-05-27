using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record SignUpRequest(
    [property: Required, EmailAddress] string Email,
    // Username is an optional display name. If empty / whitespace, the server falls back to the
    // local part of the email. MaxLength stays so storage size is bounded.
    [property: MaxLength(50)] string? Username,
    [property: Required, MinLength(6)] string Password,
    // Optional preferred UI language captured at signup so clients don't have to PATCH right
    // after. Falls back to "en" when missing or unsupported. Validated against SupportedLanguages.
    string? PreferredLanguage = null);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record RefreshRequest([property: Required] string RefreshToken);

public sealed record LogoutRequest([property: Required] string RefreshToken);

public sealed record ResendVerificationRequest([property: Required, EmailAddress] string Email);

public sealed record UserSummary(
    int Id,
    string Username,
    string Email,
    bool EmailVerified,
    string PreferredLanguage);

public sealed record UpdatePreferencesRequest(
    [property: Required] string PreferredLanguage);

public sealed record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

public sealed record GoogleCallbackRequest([property: Required] string IdToken);
