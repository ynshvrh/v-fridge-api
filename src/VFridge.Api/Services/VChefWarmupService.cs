namespace VFridge.Api.Services;

/// <summary>
/// Background worker responsible for keeping the V-Chef microservice warm (preventing Render free tier spin-down)
/// and providing on-demand health pings.
/// Periodic interval: 14 minutes.
/// </summary>
public sealed class VChefWarmupService(IVChefClient vChef, ILogger<VChefWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan WarmupInterval = TimeSpan.FromMinutes(14);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial startup delay to allow host port binding
        await Task.Delay(1000, stoppingToken);

        logger.LogInformation("VChefWarmupService started. Warmup interval: {Interval} minutes", WarmupInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await PingOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(WarmupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task PingOnceAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            await vChef.PingHealthAsync(cts.Token);
            logger.LogInformation("V-Chef microservice warmup ping completed successfully");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "V-Chef microservice warmup ping failed or timed out");
        }
    }
}
