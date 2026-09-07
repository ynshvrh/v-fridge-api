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

            var chatHistory = history
                .Select(h => new VChefChatHistoryItem(h.Role, h.Content))
                .ToList();

            var chatRequest = new VChefChatRequest(
                History: chatHistory,
                Message: userPrompt,
                Inventory: ingredients,
                Language: string.IsNullOrWhiteSpace(language) ? "uk" : language,
                CuisinePreference: cuisinePreference ?? "",
                DietaryProfile: dietaryProfile);

            var chatResponse = await vChef.ChatAsync(chatRequest, ct);
            if (chatResponse is null)
            {
                logger.LogWarning("VChef microservice returned empty chat response");
                return null;
            }

            object? recipeObj = null;
            var shoppingSuggestions = new List<object>();

            if (chatResponse.Recipe is not null && !string.IsNullOrWhiteSpace(chatResponse.Recipe.Title))
            {
                var recipe = chatResponse.Recipe;
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

                recipeObj = new
                {
                    name = recipe.Title,
                    description = recipe.Description,
                    ingredients = parsedIngredients.Select(i =>
                        i.Quantity.HasValue && !string.IsNullOrWhiteSpace(i.Unit)
                            ? $"{i.Quantity.Value} {i.Unit} {i.CleanName}"
                            : (i.Quantity.HasValue ? $"{i.Quantity.Value} {UnitStandards.ToDisplayUnit("pcs", language)} {i.CleanName}" : i.CleanName))
                        .ToList(),
                    steps = recipe.Steps,
                    calories = cal,
                    protein = prot,
                    fat = fat,
                    carbs = carbs,
                    portions = portions
                };

                // If explicit shopping suggestions were not returned, deduce from ingredients not in fridge
                if (chatResponse.ShoppingSuggestions == null || chatResponse.ShoppingSuggestions.Count == 0)
                {
                    var missing = recipe.Ingredients
                        .Zip(parsedIngredients, (raw, parsed) => (raw, parsed))
                        .Where(pair => !pair.raw.InFridge)
                        .Select(pair => new
                        {
                            name = pair.parsed.CleanName,
                            quantity = pair.parsed.Quantity ?? 1,
                            unit = UnitStandards.Normalize(pair.parsed.Unit) switch
                            {
                                "" or null => "pcs",
                                var u => u
                            },
                            category = CategoryInferrer.InferCategory(pair.parsed.CleanName)
                        });
                    shoppingSuggestions.AddRange(missing);
                }
            }

            if (chatResponse.ShoppingSuggestions != null && chatResponse.ShoppingSuggestions.Count > 0)
            {
                foreach (var s in chatResponse.ShoppingSuggestions)
                {
                    var parsed = IngredientDeductionHelper.Parse(s.Name, s.Quantity?.ToString(System.Globalization.CultureInfo.InvariantCulture), s.Unit);
                    shoppingSuggestions.Add(new
                    {
                        name = parsed.CleanName,
                        quantity = parsed.Quantity ?? 1,
                        unit = UnitStandards.Normalize(parsed.Unit) switch
                        {
                            "" or null => "pcs",
                            var u => u
                        },
                        category = CategoryInferrer.InferCategory(parsed.CleanName)
                    });
                }
            }

            var replyMessage = chatResponse.Reply;
            if (string.IsNullOrWhiteSpace(replyMessage) && recipeObj != null)
            {
                replyMessage = language == "en"
                    ? "Here is a recipe based on your ingredients:"
                    : "Ось чудовий рецепт на основі ваших продуктів:";
            }

            var structuredResponse = new
            {
                message = replyMessage,
                recipe = recipeObj,
                suggestedShoppingItems = shoppingSuggestions
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
