using System.Diagnostics;
using AnalyticsService.Data;
using Microsoft.EntityFrameworkCore;

namespace AnalyticsService;

public sealed class StatsRecalculator(IServiceScopeFactory scopeFactory, ILogger<StatsRecalculator> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var stopwatch = Stopwatch.StartNew();
        await db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_RecalculateLinkStats", ct);
        logger.LogInformation("Recalculated link stats in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
    }
}

public sealed class StatsRecalculationService(StatsRecalculator recalculator, IConfiguration config, ILogger<StatsRecalculationService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Analytics:RecalculateIntervalSeconds", 30));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await recalculator.RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Link stats recalculation failed; retrying next tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
