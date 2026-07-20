using System;

namespace VFridge.Api.Data.Entities;

public class SavedRecipeRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? FridgeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string IngredientsJson { get; set; } = "[]";
    public string StepsJson { get; set; } = "[]";
    public int Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Fat { get; set; }
    public decimal Carbs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User? User { get; set; }
}
