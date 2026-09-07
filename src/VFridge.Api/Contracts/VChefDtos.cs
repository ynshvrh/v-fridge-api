using System.Text.Json.Serialization;

namespace VFridge.Api.Contracts;

public sealed record VChefGenerateRecipeRequest(
    [property: JsonPropertyName("ingredients")] List<string> Ingredients,
    [property: JsonPropertyName("meal_type")] string? MealType = null,
    [property: JsonPropertyName("dietary_category")] string? DietaryCategory = null,
    [property: JsonPropertyName("max_prep_time_mins")] int? MaxPrepTimeMins = null,
    [property: JsonPropertyName("target_calories")] int? TargetCalories = null);

public sealed record VChefIngredient(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] decimal? Quantity,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("in_fridge")] bool InFridge);

public sealed record VChefRecipeResponse(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("prep_time_mins")] int PrepTimeMins,
    [property: JsonPropertyName("cook_time_mins")] int CookTimeMins,
    [property: JsonPropertyName("servings")] int Servings,
    [property: JsonPropertyName("calories")] int Calories,
    [property: JsonPropertyName("protein_grams")] decimal ProteinGrams,
    [property: JsonPropertyName("fat_grams")] decimal FatGrams,
    [property: JsonPropertyName("carbs_grams")] decimal CarbsGrams,
    [property: JsonPropertyName("ingredients")] List<VChefIngredient> Ingredients,
    [property: JsonPropertyName("steps")] List<string> Steps,
    [property: JsonPropertyName("generated_at")] DateTime GeneratedAt);

public sealed record VChefChatHistoryItem(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

public sealed record VChefChatRequest(
    [property: JsonPropertyName("history")] List<VChefChatHistoryItem> History,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("inventory")] List<string> Inventory,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("cuisine_preference")] string CuisinePreference,
    [property: JsonPropertyName("dietary_profile")] string? DietaryProfile);

public sealed record VChefChatResponse(
    [property: JsonPropertyName("reply")] string Reply,
    [property: JsonPropertyName("recipe")] VChefRecipeResponse? Recipe,
    [property: JsonPropertyName("shopping_suggestions")] List<VChefIngredient>? ShoppingSuggestions);

