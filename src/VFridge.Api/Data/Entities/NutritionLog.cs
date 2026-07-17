using System;

namespace VFridge.Api.Data.Entities;

public sealed class NutritionLog
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string MealType { get; set; } = null!; // 'breakfast', 'lunch', 'dinner', 'snack'
    public string FoodName { get; set; } = null!;
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public int Calories { get; set; }
    public decimal Protein { get; set; }
    public decimal Fat { get; set; }
    public decimal Carbs { get; set; }
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
