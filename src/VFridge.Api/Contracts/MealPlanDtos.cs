namespace VFridge.Api.Contracts;

public sealed record MealPlanResponse(
    IReadOnlyList<MealPlanMeal> Meals,
    IReadOnlyList<MealPlanGapItem> GapItems,
    DateTime GeneratedAt);

public sealed record MealPlanMeal(
    string Name,
    string Day,
    IReadOnlyList<string> Ingredients,
    string? Note,
    // Added after the original four fields, with defaults, so existing positional
    // constructions keep compiling and old cached plans (which lack these keys)
    // deserialize cleanly to null.
    string? Description = null,
    IReadOnlyList<string>? Steps = null,
    string? MealType = null,
    int Calories = 0,
    decimal Protein = 0,
    decimal Fat = 0,
    decimal Carbs = 0);

public sealed record MealPlanGapItem(
    string Name,
    string? Quantity,
    string? Unit,
    string Category);
