namespace VFridge.Api.Data.Entities;

public sealed class EmailVerification
{
    public int UserId { get; set; }
    public DateTime VerifiedAt { get; set; }

    public User User { get; set; } = null!;
}
