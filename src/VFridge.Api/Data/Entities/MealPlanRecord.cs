namespace VFridge.Api.Data.Entities;

/// <summary>
/// Cached meal plan for a single fridge. Stored as raw JSON blobs so we don't
/// need to model every Meal/GapItem field at the schema level — the LLM
/// response shape can evolve without a migration. Exactly one row per fridge
/// (UNIQUE on FridgeId); regeneration is an UPSERT.
/// </summary>
public sealed class MealPlanRecord
{
    public int Id { get; set; }
    public int FridgeId { get; set; }
    public string MealsJson { get; set; } = null!;
    public string GapItemsJson { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Fridge Fridge { get; set; } = null!;
}
