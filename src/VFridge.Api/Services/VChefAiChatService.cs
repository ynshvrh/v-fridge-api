using System.Text.Json;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public sealed class VChefAiChatService(
    IVChefClient vChef,
    ILogger<VChefAiChatService> logger) : IAiChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<string?> GenerateReplyAsync(
        IReadOnlyList<(string Role, string Content)> history,
        string fridgeInventory,
        string userPrompt,
        string cuisinePreference,
        string language,
        string? dietaryProfile,
        CancellationToken ct)
    {
        try
        {
            // Parse available ingredients from inventory string
            var ingredients = new List<string>();
            if (!string.IsNullOrWhiteSpace(fridgeInventory) && !fridgeInventory.Contains("empty", StringComparison.OrdinalIgnoreCase))
            {
                ingredients = fridgeInventory
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(i => i.Split('[')[0].Trim())
                    .Where(i => !string.IsNullOrWhiteSpace(i))
                    .ToList();
            }

            if (ingredients.Count == 0)
            {
                ingredients.Add("any available basics");
            }

            var request = new VChefGenerateRecipeRequest(
                Ingredients: ingredients,
                MealType: "lunch",
                DietaryCategory: dietaryProfile ?? "any",
                MaxPrepTimeMins: 30,
                TargetCalories: 500);

            var recipe = await vChef.GenerateRecipeAsync(request, ct);
            if (recipe is null)
            {
                logger.LogWarning("VChef microservice returned empty recipe response");
                return null;
            }

            var structuredResponse = new
            {
                message = $"Ось чудовий рецепт на основі ваших продуктів: {recipe.Title}",
                recipe = new
                {
                    name = recipe.Title,
                    description = recipe.Description,
                    ingredients = recipe.Ingredients.Select(i => 
                        i.Quantity.HasValue ? $"{i.Quantity.Value}{i.Unit} {i.Name}" : i.Name).ToList(),
                    steps = recipe.Steps,
                    calories = recipe.Calories,
                    protein = (int)Math.Round(recipe.ProteinGrams),
                    fat = (int)Math.Round(recipe.FatGrams),
                    carbs = (int)Math.Round(recipe.CarbsGrams),
                    portions = recipe.Servings > 0 ? recipe.Servings : 2
                },
                suggestedShoppingItems = recipe.Ingredients
                    .Where(i => !i.InFridge)
                    .Select(i => new
                    {
                        name = i.Name,
                        quantity = i.Quantity ?? 1,
                        unit = i.Unit ?? "шт",
                        category = "other"
                    })
                    .ToList()
            };

            return JsonSerializer.Serialize(structuredResponse, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate reply via V-Chef service");
            return null;
        }
    }
}
