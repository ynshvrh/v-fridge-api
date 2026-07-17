using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

public sealed class FakeMealPlannerService : IMealPlannerService
{
    // Light plan: no Description/Steps, mirroring the real service. Recipes are filled in lazily.
    public MealPlanResponse? Response { get; set; } = new(
        new List<MealPlanMeal>
        {
            new("Tomato pasta", "Monday", new[] { "pasta", "tomato sauce" }, "Quick weeknight", MealType: "lunch"),
            new("Cheese omelette", "Tuesday", new[] { "eggs", "cheese" }, null, MealType: "breakfast"),
        },
        new List<MealPlanGapItem>
        {
            new("pasta", "200", "g", "pantry"),
            new("tomato sauce", "1", "jar", "sauces"),
        },
        DateTime.UtcNow);

    public MealPlanMeal? RegeneratedMeal { get; set; } = new(
        "Borscht", "Monday", new[] { "beetroot", "cabbage", "potato" }, "Sour-cream on top",
        "Hearty Ukrainian beet soup", new[] { "Boil the broth", "Add beetroot and cabbage", "Simmer 20 min" },
        MealType: "lunch");

    public int CallCount { get; private set; }

    public string? LastCuisinePreference { get; private set; }

    public string? LastLanguage { get; private set; }

    public string? LastRegeneratedDay { get; private set; }

    public IReadOnlyList<string>? LastAvoidMealNames { get; private set; }

    public string? LastDietaryProfile { get; private set; }

    public MealRecipe? Recipe { get; set; } = new(
        "A quick comforting dish", new[] { "Prep the ingredients", "Cook for 15 minutes", "Serve hot" });

    public int RecipeCallCount { get; private set; }

    public string? LastRecipeMealName { get; private set; }

    public string? LastRecipeLanguage { get; private set; }

    public Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string? dietaryProfile,
        CancellationToken ct)
    {
        CallCount++;
        LastCuisinePreference = cuisinePreference;
        LastLanguage = language;
        LastDietaryProfile = dietaryProfile;
        return Task.FromResult(Response);
    }

    public Task<IReadOnlyList<MealPlanMeal>?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        string? dietaryProfile,
        CancellationToken ct)
    {
        CallCount++;
        LastCuisinePreference = cuisinePreference;
        LastLanguage = language;
        LastRegeneratedDay = day;
        LastAvoidMealNames = avoidMealNames;
        LastDietaryProfile = dietaryProfile;
        // Mirror the real service: the returned meal's Day is pinned to the requested day.
        var meal = RegeneratedMeal;
        IReadOnlyList<MealPlanMeal>? list = meal is null ? null : new List<MealPlanMeal> { meal with { Day = day } };
        return Task.FromResult(list);
    }

    public Task<MealRecipe?> GenerateRecipeAsync(
        string mealName,
        IReadOnlyList<string> ingredients,
        string language,
        CancellationToken ct)
    {
        RecipeCallCount++;
        LastRecipeMealName = mealName;
        LastRecipeLanguage = language;
        return Task.FromResult(Recipe);
    }
}
