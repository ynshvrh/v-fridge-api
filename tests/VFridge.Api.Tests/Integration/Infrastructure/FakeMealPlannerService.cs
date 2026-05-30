using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

public sealed class FakeMealPlannerService : IMealPlannerService
{
    public MealPlanResponse? Response { get; set; } = new(
        new List<MealPlanMeal>
        {
            new("Tomato pasta", "Monday", new[] { "pasta", "tomato sauce" }, "Quick weeknight"),
            new("Cheese omelette", "Tuesday", new[] { "eggs", "cheese" }, null),
        },
        new List<MealPlanGapItem>
        {
            new("pasta", "200", "g", "pantry"),
            new("tomato sauce", "1", "jar", "sauces"),
        },
        DateTime.UtcNow);

    public MealPlanMeal? RegeneratedMeal { get; set; } = new(
        "Borscht", "Monday", new[] { "beetroot", "cabbage", "potato" }, "Sour-cream on top",
        "Hearty Ukrainian beet soup", new[] { "Boil the broth", "Add beetroot and cabbage", "Simmer 20 min" });

    public int CallCount { get; private set; }

    public string? LastCuisinePreference { get; private set; }

    public string? LastLanguage { get; private set; }

    public string? LastRegeneratedDay { get; private set; }

    public IReadOnlyList<string>? LastAvoidMealNames { get; private set; }

    public Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        CancellationToken ct)
    {
        CallCount++;
        LastCuisinePreference = cuisinePreference;
        LastLanguage = language;
        return Task.FromResult(Response);
    }

    public Task<MealPlanMeal?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        CancellationToken ct)
    {
        CallCount++;
        LastCuisinePreference = cuisinePreference;
        LastLanguage = language;
        LastRegeneratedDay = day;
        LastAvoidMealNames = avoidMealNames;
        // Mirror the real service: the returned meal's Day is pinned to the requested day.
        var meal = RegeneratedMeal;
        return Task.FromResult(meal is null ? null : meal with { Day = day });
    }
}
