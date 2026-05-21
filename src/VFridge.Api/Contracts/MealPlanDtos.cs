namespace VFridge.Api.Contracts;

public sealed record MealPlanResponse(
    IReadOnlyList<MealPlanMeal> Meals,
    IReadOnlyList<MealPlanGapItem> GapItems);

public sealed record MealPlanMeal(
    string Name,
    string Day,
    IReadOnlyList<string> Ingredients,
    string? Note);

public sealed record MealPlanGapItem(
    string Name,
    string? Quantity,
    string? Unit,
    string Category);
