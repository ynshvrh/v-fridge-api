using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public interface IMealPlannerService
{
    /// <summary>
    /// Generates a 5-meal plan from the supplied inventory, steered by the user's cuisine
    /// preference and written in their preferred language (human-readable fields only — the
    /// <c>day</c> and <c>category</c> codes stay English). Returns null when the AI provider
    /// is unavailable or returns a malformed payload.
    /// </summary>
    Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        CancellationToken ct);

    /// <summary>
    /// Regenerates a single meal for the given weekday, steered by cuisine + language and avoiding the
    /// supplied dish names so the new meal differs from the rest of the plan. The returned meal's
    /// <c>Day</c> is pinned to <paramref name="day"/>. Returns null when the provider is unavailable
    /// or returns a malformed payload.
    /// </summary>
    Task<MealPlanMeal?> RegenerateDayAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        string cuisinePreference,
        string language,
        string day,
        IReadOnlyList<string> avoidMealNames,
        CancellationToken ct);
}

public sealed record MealPlanInventoryItem(string Name, decimal Quantity, string Unit, string Category);
