namespace VFridge.Api.Features.SavedRecipes;

public sealed record SavedRecipeResponse(
    int Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Ingredients,
    IReadOnlyList<string> Steps,
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs,
    DateTime CreatedAt);

public sealed record SaveRecipeRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? Ingredients,
    IReadOnlyList<string>? Steps,
    int Calories,
    decimal Protein,
    decimal Fat,
    decimal Carbs);
