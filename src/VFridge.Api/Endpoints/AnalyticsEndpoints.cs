using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").WithTags("Analytics");

        group.MapGet("/", GetSummaryAsync)
            .WithName("GetAnalyticsSummary")
            .WithSummary("Dashboard analytics for the active fridge")
            .WithDescription("Aggregates the consumption_log over the last 30 days: most-wasted items, fastest-consumed items, and a weekly count of consumed-vs-wasted-vs-expired rows for the last 8 weeks.")
            .Produces<AnalyticsSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetSummaryAsync(VFridgeDbContext db, FridgeContext fridgeContext, CancellationToken ct)
    {
        var resolved = await fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var eightWeeksAgo = now.AddDays(-56);

        var wastedRows = await db.ConsumptionLogs
            .Where(c => c.FridgeId == fridgeId
                        && c.ConsumedAt >= thirtyDaysAgo
                        && (c.Status == ConsumptionStatus.Wasted || c.Status == ConsumptionStatus.Expired))
            .Select(c => new { c.ProductName, c.Quantity, c.Category })
            .ToListAsync(ct);

        var mostWasted = wastedRows
            .GroupBy(x => x.ProductName)
            .Select(g => new AnalyticsLeader(
                g.Key,
                g.Sum(x => x.Quantity ?? 0m),
                g.Count(),
                g.Select(x => x.Category).FirstOrDefault() ?? ProductCategories.Other))
            .OrderByDescending(x => x.TotalQuantity)
            .ThenByDescending(x => x.Occurrences)
            .Take(5)
            .ToList();

        var fastestConsumed = await db.ConsumptionLogs
            .Where(c => c.FridgeId == fridgeId
                        && c.Status == ConsumptionStatus.Consumed
                        && c.ConsumedAt >= thirtyDaysAgo
                        && c.AgeDays != null)
            .OrderBy(c => c.AgeDays)
            .ThenByDescending(c => c.ConsumedAt)
            .Take(5)
            .Select(c => new FastestConsumed(c.ProductName, c.Category, c.AgeDays!.Value))
            .ToListAsync(ct);

        var weekly = await db.ConsumptionLogs
            .Where(c => c.FridgeId == fridgeId && c.ConsumedAt >= eightWeeksAgo)
            .ToListAsync(ct);

        // Bucket into ISO weeks client-side (after the round-trip) so EF doesn't have to translate
        // a window function — the data set is small (one user, last 8 weeks).
        var weeklyTrends = weekly
            .GroupBy(c =>
            {
                var d = c.ConsumedAt ?? DateTime.UtcNow;
                // Monday-of-week, 0 if it is already Monday, 6 if it is Sunday.
                var offset = ((int)d.DayOfWeek + 6) % 7;
                var monday = d.AddDays(-offset);
                return new DateTime(monday.Year, monday.Month, monday.Day);
            })
            .OrderBy(g => g.Key)
            .Select(g => new WeeklyTrend(
                g.Key.ToString("yyyy-MM-dd"),
                g.Count(x => x.Status == ConsumptionStatus.Consumed),
                g.Count(x => x.Status == ConsumptionStatus.Wasted),
                g.Count(x => x.Status == ConsumptionStatus.Expired)))
            .ToList();

        var summary = new AnalyticsSummary(mostWasted, fastestConsumed, weeklyTrends);
        return Results.Ok(summary);
    }
}
