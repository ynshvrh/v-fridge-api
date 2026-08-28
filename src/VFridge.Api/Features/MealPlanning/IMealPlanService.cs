using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.MealPlanning;

public interface IMealPlanService
{
    Task<IResult> GetCachedAsync(CancellationToken ct);
    Task<IResult> GenerateAsync(CancellationToken ct);
    Task<IResult> RegenerateDayAsync(RegenerateDayRequest req, CancellationToken ct);
    Task<IResult> RegenerateMealAsync(RegenerateMealRequest req, CancellationToken ct);
    Task<IResult> GetRecipeAsync(GetRecipeRequest req, CancellationToken ct);
    Task<IResult> ImportGapsAsync(ImportGapsRequest req, CancellationToken ct);
}
