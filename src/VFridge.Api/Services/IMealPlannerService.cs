using VFridge.Api.Contracts;

namespace VFridge.Api.Services;

public interface IMealPlannerService
{
    /// <summary>
    /// Generates a 5-meal plan from the supplied inventory. Returns null when the AI provider
    /// is unavailable or returns a malformed payload.
    /// </summary>
    Task<MealPlanResponse?> GenerateAsync(
        IReadOnlyList<MealPlanInventoryItem> inventory,
        CancellationToken ct);
}

public sealed record MealPlanInventoryItem(string Name, decimal Quantity, string Unit, string Category);
