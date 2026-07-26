using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record SignUpRequest(
    [property: Required, EmailAddress] string Email,
    // Username is an optional display name. If empty / whitespace, the server falls back to the
    // local part of the email. MaxLength stays so storage size is bounded.
    [property: MaxLength(50)] string? Username,
    [property: Required, MinLength(8), MaxLength(72)] string Password,
    // Optional preferred UI language captured at signup so clients don't have to PATCH right
    // after. Falls back to "en" when missing or unsupported. Validated against SupportedLanguages.
    string? PreferredLanguage = null,
    // Optional cuisine preference (used by the chef to steer recipe suggestions). Falls back to
    // "any" when missing or unsupported. Validated against SupportedCuisines.
    string? CuisinePreference = null);

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
    string PreferredLanguage,
    string CuisinePreference,
    string? DietaryProfile = null,
    string? Avatar = null);

public sealed record UpdateProfileRequest(
    [property: MaxLength(50)] string? Username = null,
    string? Avatar = null,
    [property: MinLength(8), MaxLength(72)] string? NewPassword = null,
    string? CurrentPassword = null);

/// <summary>
/// Partial-update DTO for /auth/me/preferences. Both fields are optional; only the ones
/// the client sends are applied. Sending neither is a no-op.
/// </summary>
public sealed record UpdatePreferencesRequest(
    string? PreferredLanguage = null,
    string? CuisinePreference = null,
    string? DietaryProfile = null);

public sealed record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

public sealed record GoogleCallbackRequest([property: Required] string IdToken);
