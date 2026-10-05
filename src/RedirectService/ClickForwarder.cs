using System.Diagnostics.Metrics;
using System.Threading.Channels;
using TinyLink.Contracts;
using TinyLink.ServiceDefaults;

namespace RedirectService;

// Redirects enqueue clicks and return immediately; this drains the queue to the analytics service.
// The queue is bounded and drops the oldest clicks when full, so a slow analytics service can never
// stall redirects or exhaust memory.
public sealed class ClickQueue
{
    private static readonly Counter<long> Enqueued = TinyLinkMetrics.Meter.CreateCounter<long>("tinylink.clicks.enqueued");
    public static readonly Counter<long> Dropped = TinyLinkMetrics.Meter.CreateCounter<long>(
        "tinylink.clicks.dropped", description: "Clicks never delivered to analytics, by reason: queue_full, send_failed, shutdown");

    private readonly Channel<RecordClickRequest> _channel;

    public ClickQueue()
    {
        _channel = Channel.CreateBounded<RecordClickRequest>(
            new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest },
            _ => Dropped.Add(1, new KeyValuePair<string, object?>("reason", "queue_full")));
        TinyLinkMetrics.Meter.CreateObservableGauge("tinylink.clicks.queue_depth", () => _channel.Reader.Count);
    }

    public void Enqueue(RecordClickRequest click)
    {
        if (_channel.Writer.TryWrite(click))
        {
            Enqueued.Add(1);
        }
        else
        {
            Dropped.Add(1, new KeyValuePair<string, object?>("reason", "shutdown"));
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    public ChannelReader<RecordClickRequest> Reader => _channel.Reader;
}

// Sends clicks in batches: under load each request carries up to MaxBatchSize clicks, and when traffic
// is light a batch is just whatever is waiting (often one click), so there is no added delay.
// On shutdown (e.g. the HPA scaling in) the queue is flushed for up to DrainTimeout instead of being lost.
public sealed class ClickForwarder(ClickQueue queue, IHttpClientFactory httpClientFactory, ILogger<ClickForwarder> logger)
    : BackgroundService
{
    public const string HttpClientName = "analytics";
    private const int Workers = 2;
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _drainDeadline = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => ForwardAsync(_drainDeadline.Token)));

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        _drainDeadline.CancelAfter(DrainTimeout);
        await base.StopAsync(cancellationToken);
        logger.LogInformation("Click forwarder stopped; {Remaining} clicks left unsent", queue.Reader.Count);
    }

    public override void Dispose()
    {
        _drainDeadline.Dispose();
        base.Dispose();
    }

    // Runs until the queue is completed and empty, or the drain deadline passes.
    private async Task ForwardAsync(CancellationToken deadline)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var batch = new List<RecordClickRequest>(MaxBatchSize);

        try
        {
            while (await queue.Reader.WaitToReadAsync(deadline))
            {
                while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out var click))
                {
                    batch.Add(click);
                }

                if (batch.Count > 0)
                {
                    await SendAsync(http, batch, deadline);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            // Drain deadline reached during shutdown; whatever is left is reported by StopAsync.
        }
    }

    private async Task SendAsync(HttpClient http, List<RecordClickRequest> batch, CancellationToken deadline)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/clicks/batch", new RecordClicksBatchRequest(batch),
                AppJsonContext.Default.RecordClicksBatchRequest, deadline);
            if (response.IsSuccessStatusCode)
            {
                Forwarded.Add(batch.Count);
                return;
            }

            logger.LogWarning("Analytics rejected a batch of {Count} clicks: {Status}", batch.Count, (int)response.StatusCode);
        }
        catch (Exception ex) when (!deadline.IsCancellationRequested)
        {
            logger.LogWarning("Dropped a batch of {Count} clicks: {Error}", batch.Count, ex.Message);
        }

        ClickQueue.Dropped.Add(batch.Count, new KeyValuePair<string, object?>("reason", "send_failed"));
    }

    private static readonly Counter<long> Forwarded = TinyLinkMetrics.Meter.CreateCounter<long>("tinylink.clicks.forwarded");
}
