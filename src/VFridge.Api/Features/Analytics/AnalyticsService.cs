using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VFridge.Api.Auth;
using VFridge.Api.Contracts;
using VFridge.Api.Data;
using VFridge.Api.Data.Entities;
using VFridge.Api.Services;

namespace VFridge.Api.Features.Analytics;

public class AnalyticsService : IAnalyticsService
{
    private readonly VFridgeDbContext _db;
    private readonly FridgeContext _fridgeContext;

    public AnalyticsService(VFridgeDbContext db, FridgeContext fridgeContext)
    {
        _db = db;
        _fridgeContext = fridgeContext;
    }

    public async Task<IResult> GetSummaryAsync(CancellationToken ct)
    {
        var resolved = await _fridgeContext.ResolveAsync(ct);
        if (resolved is null) return Results.Unauthorized();

        var fridgeId = resolved.Value.FridgeId;
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var eightWeeksAgo = now.AddDays(-56);

        var wastedRows = await _db.ConsumptionLogs
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

        var fastestConsumed = await _db.ConsumptionLogs
            .Where(c => c.FridgeId == fridgeId
                        && c.Status == ConsumptionStatus.Consumed
                        && c.ConsumedAt >= thirtyDaysAgo
                        && c.AgeDays != null)
            .OrderBy(c => c.AgeDays)
            .ThenByDescending(c => c.ConsumedAt)
            .Take(5)
            .Select(c => new FastestConsumed(c.ProductName, c.Category, c.AgeDays!.Value))
            .ToListAsync(ct);

        var weekly = await _db.ConsumptionLogs
            .Where(c => c.FridgeId == fridgeId && c.ConsumedAt >= eightWeeksAgo)
            .ToListAsync(ct);

        var weeklyTrends = weekly
            .GroupBy(c =>
            {
                var d = c.ConsumedAt ?? DateTime.UtcNow;
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
