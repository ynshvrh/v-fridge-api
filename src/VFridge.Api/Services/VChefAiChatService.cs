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

            // Normalize and parse all ingredients cleanly
            var parsedIngredients = recipe.Ingredients
                .Select(i => IngredientDeductionHelper.Parse(
                    i.Name,
                    i.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    i.Unit))
                .ToList();

            var portions = recipe.Servings > 0 ? recipe.Servings : 2;

            // Deterministic Nutrition Calculation (fall back or validate)
            int cal = recipe.Calories;
            int prot = (int)Math.Round(recipe.ProteinGrams);
            int fat = (int)Math.Round(recipe.FatGrams);
            int carbs = (int)Math.Round(recipe.CarbsGrams);

            if (cal <= 0 || (prot == 0 && fat == 0 && carbs == 0))
            {
                var calc = NutritionCalculator.CalculateNutrition(parsedIngredients, portions);
                cal = calc.Calories;
                prot = (int)Math.Round(calc.Protein);
                fat = (int)Math.Round(calc.Fat);
                carbs = (int)Math.Round(calc.Carbs);
            }

            var structuredResponse = new
            {
                message = $"Ось чудовий рецепт на основі ваших продуктів: {recipe.Title}",
                recipe = new
                {
                    name = recipe.Title,
                    description = recipe.Description,
                    ingredients = parsedIngredients.Select(i =>
                        i.Quantity.HasValue && !string.IsNullOrWhiteSpace(i.Unit)
                            ? $"{i.Quantity.Value} {i.Unit} {i.CleanName}"
                            : (i.Quantity.HasValue ? $"{i.Quantity.Value} шт {i.CleanName}" : i.CleanName))
                        .ToList(),
                    steps = recipe.Steps,
                    calories = cal,
                    protein = prot,
                    fat = fat,
                    carbs = carbs,
                    portions = portions
                },
                suggestedShoppingItems = recipe.Ingredients
                    .Zip(parsedIngredients, (raw, parsed) => (raw, parsed))
                    .Where(pair => !pair.raw.InFridge)
                    .Select(pair => new
                    {
                        name = pair.parsed.CleanName,
                        quantity = pair.parsed.Quantity ?? 1,
                        unit = IngredientDeductionHelper.NormalizeUnit(pair.parsed.Unit) switch
                        {
                            "" or null => "шт",
                            var u => u
                        },
                        category = CategoryInferrer.InferCategory(pair.parsed.CleanName)
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
