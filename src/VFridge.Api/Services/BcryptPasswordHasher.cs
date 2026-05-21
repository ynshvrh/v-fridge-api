namespace VFridge.Api.Services;

/// <summary>
/// BCrypt-based hasher. Matches the Next.js bcryptjs output (same $2a$ / $2b$ scheme),
/// so users who registered through the old API can still sign in here.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 10;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;
        }
    }
}
