namespace VFridge.Api.Contracts;

public sealed record VChefGenerateRecipeRequest(
    List<string> Ingredients,
    string? MealType = null,
    string? DietaryCategory = null,
    int? MaxPrepTimeMins = null,
    int? TargetCalories = null);

public sealed record VChefIngredient(
    string Name,
    decimal? Quantity,
    string? Unit,
    bool InFridge);

public sealed record VChefRecipeResponse(
    string Title,
    string Description,
    int PrepTimeMins,
    int CookTimeMins,
    int Servings,
    int Calories,
    decimal ProteinGrams,
    decimal FatGrams,
    decimal CarbsGrams,
    List<VChefIngredient> Ingredients,
    List<string> Steps,
    DateTime GeneratedAt);
