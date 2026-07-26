using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VFridge.Api.Configuration;
using VFridge.Api.Data;

namespace VFridge.Api.Services;

/// <summary>
/// Daily maintenance worker. Two passes per trigger:
/// <list type="bullet">
/// <item>Email an expiry digest to every user that owns a product with
/// <c>expiry_date &lt;= today + 2 days</c> (one email per user, all items in one message).</item>
/// <item>Delete <c>users</c> rows older than 7 days that have no <c>email_verifications</c> row
/// (anti-spam cleanup). Cascades take care of the dependent rows.</item>
/// </list>
/// Fires at 09:00 Europe/Kyiv each day. No off-switch in v1; the test fixture pokes
/// <see cref="RunOnceAsync"/> directly so it does not have to wait 24 h.
/// </summary>
public sealed class DailyMaintenanceWorker(
    IServiceProvider services,
    IEmailSender email,
    IOptions<FrontendOptions> frontend,
    ILogger<DailyMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeZoneInfo KyivTz = ResolveKyivTimezone();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNext0900Kyiv(DateTimeOffset.UtcNow);
            logger.LogInformation("DailyMaintenanceWorker sleeping for {Delay} until next 09:00 Europe/Kyiv tick", delay);
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DailyMaintenanceWorker tick failed");
            }
        }
    }

    /// <summary>Runs both passes once. Exposed for tests so they can trigger it on demand.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VFridgeDbContext>();

        await SendExpiryDigestsAsync(db, ct);
        await DeleteUnverifiedStaleUsersAsync(db, ct);
    }

    private async Task SendExpiryDigestsAsync(VFridgeDbContext db, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var threshold = today.AddDays(2);

        var expiringProducts = await db.Products
            .Where(p => p.ExpiryDate != null && p.ExpiryDate <= threshold)
            .OrderBy(p => p.ExpiryDate)
            .Select(p => new
            {
                p.FridgeId,
                FridgeName = p.Fridge.Name,
                p.Name,
                p.Quantity,
                p.Unit,
                p.ExpiryDate
            })
            .ToListAsync(ct);

        if (expiringProducts.Count == 0) return;

        var fridgeIds = expiringProducts.Select(p => p.FridgeId).Distinct().ToList();
        var members = await db.FridgeMembers
            .Where(m => fridgeIds.Contains(m.FridgeId))
            .Select(m => new { m.FridgeId, m.User.Email, m.User.Username })
            .ToListAsync(ct);

        var userMembers = members
            .Where(m => !string.IsNullOrWhiteSpace(m.Email))
            .GroupBy(m => m.Email)
            .ToList();

        foreach (var userGroup in userMembers)
        {
            var emailAddress = userGroup.Key;
            var username = userGroup.First().Username;
            var userFridgeIds = userGroup.Select(m => m.FridgeId).ToHashSet();

            var userExpiringProducts = expiringProducts
                .Where(p => userFridgeIds.Contains(p.FridgeId))
                .ToList();

            if (userExpiringProducts.Count == 0) continue;

            var itemsHtml = string.Join("",
                userExpiringProducts.Select(g =>
                    $"<li><strong>{System.Net.WebUtility.HtmlEncode(g.Name)}</strong> ({System.Net.WebUtility.HtmlEncode(g.FridgeName)}) — {g.Quantity} {g.Unit}, " +
                    (g.ExpiryDate is { } d
                        ? d < today ? $"<span style=\"color:#B23A30;\">expired on {d:yyyy-MM-dd}</span>"
                                    : $"expires {d:yyyy-MM-dd}"
                        : "no date") +
                    "</li>"));

            var html = $"""
                <div style="font-family: system-ui, sans-serif; max-width:480px; margin:auto;">
                  <h2 style="color:#8C5383;">Items expiring soon</h2>
                  <p>Hi <strong>{System.Net.WebUtility.HtmlEncode(username)}</strong>, here is what to use up first:</p>
                  <ul style="line-height:1.6;">{itemsHtml}</ul>
                  <p style="color:#666;font-size:13px;">
                    Open V-Fridge to update quantities or remove items: {frontend.Value.BaseUrl}
                  </p>
                </div>
                """;

            try
            {
                await email.SendAsync(emailAddress, "V-Fridge — items expiring soon", html, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send expiry digest to {Email}", emailAddress);
            }
        }
    }

    private async Task DeleteUnverifiedStaleUsersAsync(VFridgeDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);

        // SELECT users older than the cutoff that have no email_verifications row.
        // ExecuteDeleteAsync issues a single DELETE with the EXISTS predicate; cascades
        // sweep the dependent rows (refresh_tokens, oauth_logins, products, chat, …).
        var deleted = await db.Users
            .Where(u => u.CreatedAt != null && u.CreatedAt < cutoff)
            .Where(u => !db.EmailVerifications.Any(e => e.UserId == u.Id))
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("DailyMaintenanceWorker cleaned up {Count} unverified stale users", deleted);
    }

    private static TimeSpan TimeUntilNext0900Kyiv(DateTimeOffset nowUtc)
    {
        var nowKyiv = TimeZoneInfo.ConvertTime(nowUtc, KyivTz);
        var todayAt9 = new DateTimeOffset(
            nowKyiv.Year, nowKyiv.Month, nowKyiv.Day, 9, 0, 0, nowKyiv.Offset);
        var target = nowKyiv < todayAt9 ? todayAt9 : todayAt9.AddDays(1);
        return target - nowKyiv;
    }

    private static TimeZoneInfo ResolveKyivTimezone()
    {
        // Linux uses IANA names, Windows uses its own — try both.
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* try the next id */ }
        }
        // Last-resort fallback: a static +02:00 offset (still close enough that the job
        // will fire roughly on time even when the host can't resolve a zone).
        return TimeZoneInfo.CreateCustomTimeZone("UTC+02", TimeSpan.FromHours(2), "UTC+02", "UTC+02");
    }
}
