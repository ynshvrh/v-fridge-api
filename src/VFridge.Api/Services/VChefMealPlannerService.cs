using System.Text.Json;
using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public sealed class VChefMealPlannerService(
    IVChefClient vChef,
    ILogger<VChefMealPlannerService> logger) : IMealPlannerService
{
    private static readonly List<string> DayList = new()
    {
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    };

    public async Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string? dietaryProfile,
        string? currentDay,
        IReadOnlyList<MealPlanMeal>? existingMeals,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentDay))
            {
                currentDay = DateTime.UtcNow.DayOfWeek.ToString();
            }

            var invNames = inventory.Select(i => $"{i.Quantity} {i.Unit} {i.Name}").ToList();
            var existingNames = existingMeals?.Select(m => $"{m.Name} ({m.Day})").ToList() ?? new();

            var req = new VChefMealPlanRequest(
                Inventory: invNames,
                CuisinePreference: cuisinePreference,
                Language: language,
                DietaryProfile: dietaryProfile,
                Day: currentDay,
                ExistingMeals: existingNames);

            var vChefResp = await vChef.GenerateMealPlanAsync(req, ct);
            if (vChefResp is null || vChefResp.Meals is null || vChefResp.Meals.Count == 0)
            {
                logger.LogWarning("VChef microservice returned null or empty meal plan response");
                return null;
            }

            var keptMeals = existingMeals != null
                ? existingMeals.Where(m => !m.Day.Equals(currentDay, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<MealPlanMeal>();

            var newMeals = vChefResp.Meals.Select(m => new MealPlanMeal(
                Name: m.Name,
                Day: string.IsNullOrWhiteSpace(m.Day) ? currentDay : m.Day,
                Ingredients: m.Ingredients,
                Note: m.Note,
                Description: m.Description,
                Steps: m.Steps,
                MealType: m.MealType,
                Calories: m.Calories,
                Protein: m.Protein,
                Fat: m.Fat,
                Carbs: m.Carbs)).ToList();

            var allMeals = keptMeals.Concat(newMeals)
                .OrderBy(m => DayList.FindIndex(d => d.Equals(m.Day, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var gapItems = vChefResp.GapItems?.Select(g => new MealPlanGapItem(
                Name: g.Name,
                Quantity: g.Quantity,
                Unit: g.Unit,
                Category: g.Category)).ToList() ?? new List<MealPlanGapItem>();

            return new MealPlanResponse(allMeals, gapItems, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate meal plan via VChef microservice");
            return null;
        }
    }

    public async Task<IReadOnlyList<MealPlanMeal>?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        string? dietaryProfile,
        CancellationToken ct)
    {
        try
        {
            var invNames = inventory.Select(i => $"{i.Quantity} {i.Unit} {i.Name}").ToList();

            var req = new VChefMealPlanRequest(
                Inventory: invNames,
                CuisinePreference: cuisinePreference,
                Language: language,
                DietaryProfile: dietaryProfile,
                Day: day,
                ExistingMeals: avoidMealNames.ToList());

            var vChefResp = await vChef.GenerateMealPlanAsync(req, ct);
            if (vChefResp is null || vChefResp.Meals is null || vChefResp.Meals.Count == 0)
            {
                return null;
            }

            return vChefResp.Meals.Select(m => new MealPlanMeal(
                Name: m.Name,
                Day: day,
                Ingredients: m.Ingredients,
                Note: m.Note,
                Description: m.Description,
                Steps: m.Steps,
                MealType: m.MealType,
                Calories: m.Calories,
                Protein: m.Protein,
                Fat: m.Fat,
                Carbs: m.Carbs)).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to regenerate day via VChef microservice");
            return null;
        }
    }

    public async Task<MealPlanMeal?> RegenerateMealAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        string mealType,
        IReadOnlyList<string> avoidMealNames,
        string? dietaryProfile,
        CancellationToken ct)
    {
        try
        {
            var invNames = inventory.Select(i => $"{i.Quantity} {i.Unit} {i.Name}").ToList();

            var req = new VChefMealPlanRequest(
                Inventory: invNames,
                CuisinePreference: cuisinePreference,
                Language: language,
                DietaryProfile: dietaryProfile,
                Day: day,
                MealType: mealType,
                ExistingMeals: avoidMealNames.ToList());

            var vChefResp = await vChef.GenerateMealPlanAsync(req, ct);
            if (vChefResp is null || vChefResp.Meals is null || vChefResp.Meals.Count == 0)
            {
                return null;
            }

            var m = vChefResp.Meals[0];
            return new MealPlanMeal(
                Name: m.Name,
                Day: day,
                Ingredients: m.Ingredients,
                Note: m.Note,
                Description: m.Description,
                Steps: m.Steps,
                MealType: mealType,
                Calories: m.Calories,
                Protein: m.Protein,
                Fat: m.Fat,
                Carbs: m.Carbs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to regenerate meal via VChef microservice");
            return null;
        }
    }

    public async Task<MealRecipe?> GenerateRecipeAsync(
        string mealName,
        IReadOnlyList<string> ingredients,
        string language,
        CancellationToken ct)
    {
        try
        {
            var req = new VChefGenerateRecipeRequest(
                Ingredients: ingredients.ToList(),
                MealType: null,
                DietaryCategory: null,
                MaxPrepTimeMins: 30,
                TargetCalories: 500);

            var vChefResp = await vChef.GenerateRecipeAsync(req, ct);
            if (vChefResp is null) return null;

            return new MealRecipe(
                Description: vChefResp.Description,
                Steps: vChefResp.Steps,
                Calories: vChefResp.Calories,
                Protein: vChefResp.ProteinGrams,
                Fat: vChefResp.FatGrams,
                Carbs: vChefResp.CarbsGrams);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate recipe via VChef microservice");
            return null;
        }
    }
}
