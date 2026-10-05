using System.Diagnostics;
using System.Diagnostics.Metrics;
using AnalyticsService.Data;
using Microsoft.EntityFrameworkCore;
using TinyLink.ServiceDefaults;

namespace AnalyticsService;

public sealed class StatsRecalculator(IServiceScopeFactory scopeFactory, ILogger<StatsRecalculator> logger)
{
    private static readonly Histogram<double> Duration = TinyLinkMetrics.Meter.CreateHistogram<double>(
        "tinylink.stats.recalculation.duration", unit: "s", description: "Time to run usp_RecalculateLinkStats");

    public async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnalyticsDbContext>();

        var start = Stopwatch.GetTimestamp();
        await db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_RecalculateLinkStats", ct);
        var elapsed = Stopwatch.GetElapsedTime(start);
        Duration.Record(elapsed.TotalSeconds);
        logger.LogInformation("Recalculated link stats in {ElapsedMs} ms", (long)elapsed.TotalMilliseconds);
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
