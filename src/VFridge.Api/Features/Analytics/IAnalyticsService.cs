using Microsoft.AspNetCore.Http;

namespace VFridge.Api.Features.Analytics;

public interface IAnalyticsService
{
    Task<IResult> GetSummaryAsync(CancellationToken ct);
}
