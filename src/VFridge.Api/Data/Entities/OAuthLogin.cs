namespace VFridge.Api.Data.Entities;

public sealed class OAuthLogin
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderUserId { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
