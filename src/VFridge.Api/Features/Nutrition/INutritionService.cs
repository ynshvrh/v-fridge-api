using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Nutrition;

public interface INutritionService
{
    Task<IResult> GetDailyAsync(string? date, CancellationToken ct);
    Task<IResult> LogFoodAsync(LogFoodRequest req, CancellationToken ct);
    Task<IResult> UpdateLogAsync(long id, UpdateLogRequest req, CancellationToken ct);
    Task<IResult> DeleteLogAsync(long id, CancellationToken ct);
    Task<IResult> SetTargetsAsync(SetTargetsRequest req, CancellationToken ct);
}
