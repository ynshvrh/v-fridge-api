using System.ComponentModel.DataAnnotations;

namespace VFridge.Api.Contracts;

public sealed record SignUpRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(3), MaxLength(50)] string Username,
    [Required, MinLength(6)] string Password);

public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public sealed record RefreshRequest([Required] string RefreshToken);

public sealed record LogoutRequest([Required] string RefreshToken);

public sealed record ResendVerificationRequest([Required, EmailAddress] string Email);

public sealed record UserSummary(int Id, string Username, string Email, bool EmailVerified);

public sealed record TokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

public sealed record GoogleCallbackRequest([Required] string IdToken);
