using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record SignUpRequest(
    [property: Required, EmailAddress] string Email,
    // Username is an optional display name. If empty / whitespace, the server falls back to the
    // local part of the email. MaxLength stays so storage size is bounded.
    [property: MaxLength(50)] string? Username,
    [property: Required, MinLength(6)] string Password);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record RefreshRequest([property: Required] string RefreshToken);

public sealed record LogoutRequest([property: Required] string RefreshToken);

public sealed record ResendVerificationRequest([property: Required, EmailAddress] string Email);

public sealed record UserSummary(int Id, string Username, string Email, bool EmailVerified);

public sealed record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

public sealed record GoogleCallbackRequest([property: Required] string IdToken);
