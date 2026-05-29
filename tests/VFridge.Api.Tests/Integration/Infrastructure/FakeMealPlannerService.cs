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

    public int CallCount { get; private set; }

    public string? LastCuisinePreference { get; private set; }

    public string? LastLanguage { get; private set; }

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
}
