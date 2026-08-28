using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VFridge.Api.Contracts;

namespace VFridge.Api.Features.Analytics;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").WithTags("Analytics");

        group.MapGet("/", (IAnalyticsService service, CancellationToken ct) => service.GetSummaryAsync(ct))
            .WithName("GetAnalyticsSummary")
            .WithSummary("Dashboard analytics for the active fridge")
            .WithDescription("Aggregates the consumption_log over the last 30 days: most-wasted items, fastest-consumed items, and a weekly count of consumed-vs-wasted-vs-expired rows for the last 8 weeks.")
            .Produces<AnalyticsSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}
