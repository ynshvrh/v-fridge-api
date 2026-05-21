namespace VFridge.Api.Data.Entities;

public sealed class Fridge
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int OwnerId { get; set; }
    public DateTime? CreatedAt { get; set; }

    public User Owner { get; set; } = null!;
    public ICollection<FridgeMember> Members { get; set; } = new List<FridgeMember>();
}

public sealed class FridgeMember
{
    public int FridgeId { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = FridgeRoles.Member;
    public DateTime? JoinedAt { get; set; }

    public Fridge Fridge { get; set; } = null!;
    public User User { get; set; } = null!;
}

public sealed class FridgeInvite
{
    public int Id { get; set; }
    public int FridgeId { get; set; }
    public string Email { get; set; } = null!;
    public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CreatedAt { get; set; }

    public Fridge Fridge { get; set; } = null!;

    public bool IsClaimable => AcceptedAt is null && ExpiresAt > DateTime.UtcNow;
}

public static class FridgeRoles
{
    public const string Owner = "owner";
    public const string Member = "member";
}
