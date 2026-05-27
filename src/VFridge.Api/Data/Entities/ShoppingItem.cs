using VFridge.Api.Contracts;

namespace VFridge.Api.Data.Entities;

public sealed class ShoppingItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FridgeId { get; set; }
    public string Name { get; set; } = null!;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string Category { get; set; } = ProductCategories.Other;
    public bool Checked { get; set; }
    public DateTime? CreatedAt { get; set; }

    public User User { get; set; } = null!;
}
