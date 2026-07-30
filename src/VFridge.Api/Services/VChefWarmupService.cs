namespace VFridge.Api.Services;

public sealed class VChefWarmupService(IVChefClient vChef, ILogger<VChefWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the C# web host 1 second to bind its ports before sending warmup request
        await Task.Delay(1000, stoppingToken);
        
        logger.LogInformation("Warming up V-Chef microservice on background startup...");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            cts.CancelAfter(TimeSpan.FromSeconds(25));
            await vChef.PingHealthAsync(cts.Token);
            logger.LogInformation("V-Chef microservice warmup ping completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "V-Chef microservice background warmup initiated.");
        }
    }
}
