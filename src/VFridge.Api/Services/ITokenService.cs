namespace VFridge.Api.Services;

public interface ITokenService
{
    /// <summary>Signs a short-lived JWT access token for a user.</summary>
    (string Token, DateTime ExpiresAt) IssueAccessToken(int userId, string username, string email);

    /// <summary>Generates a high-entropy refresh token (random bytes, base64url).</summary>
    string GenerateRefreshToken();

    /// <summary>Returns a SHA-256 hex digest of the raw token (what we persist).</summary>
    string Hash(string rawToken);
}
