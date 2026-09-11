using System.Threading;
using System.Threading.Tasks;
using VFridge.Api.Contracts;
using VFridge.Api.Services;

namespace VFridge.Api.Tests.Integration.Infrastructure;

public sealed class FakeVChefClient : IVChefClient
{
    public VChefNutritionEstimateResponse? NutritionEstimateResponse { get; set; } = new(
        FoodName: "Гречана каша",
        Quantity: 200,
        Unit: "г",
        Calories: 250,
        Protein: 7.0m,
        Fat: 2.0m,
        Carbs: 52.0m,
        EstimatedWeightG: 200m,
        Confidence: "ai",
        Notes: "Оцінено ШІ");

    public VChefRecipeResponse? RecipeResponse { get; set; }

    public VChefChatResponse? ChatResponse { get; set; }

    public VChefMealPlanResponse? MealPlanResponse { get; set; }

    public int NutritionCallCount { get; private set; }
    public VChefNutritionEstimateRequest? LastNutritionRequest { get; private set; }

    public Task<VChefRecipeResponse?> GenerateRecipeAsync(VChefGenerateRecipeRequest request, CancellationToken ct = default)
        => Task.FromResult(RecipeResponse);

    public Task<VChefChatResponse?> ChatAsync(VChefChatRequest request, CancellationToken ct = default)
        => Task.FromResult(ChatResponse);

    public Task<VChefMealPlanResponse?> GenerateMealPlanAsync(VChefMealPlanRequest request, CancellationToken ct = default)
        => Task.FromResult(MealPlanResponse);

    public Task<VChefNutritionEstimateResponse?> EstimateNutritionAsync(VChefNutritionEstimateRequest request, CancellationToken ct = default)
    {
        NutritionCallCount++;
        LastNutritionRequest = request;
        return Task.FromResult(NutritionEstimateResponse);
    }

    public Task PingHealthAsync(CancellationToken ct = default) => Task.CompletedTask;
}
