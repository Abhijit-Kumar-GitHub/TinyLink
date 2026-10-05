using System.Threading.Channels;
using TinyLink.Contracts;

namespace RedirectService;

// Redirects enqueue clicks and return immediately; this drains the queue to the analytics service.
// The queue is bounded and drops the oldest clicks when full, so a slow analytics service can never
// stall redirects or exhaust memory.
public sealed class ClickQueue
{
    private readonly Channel<RecordClickRequest> _channel = Channel.CreateBounded<RecordClickRequest>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest });

    public void Enqueue(RecordClickRequest click) => _channel.Writer.TryWrite(click);

    public IAsyncEnumerable<RecordClickRequest> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public sealed class ClickForwarder(ClickQueue queue, IHttpClientFactory httpClientFactory, ILogger<ClickForwarder> logger)
    : BackgroundService
{
    public const string HttpClientName = "analytics";
    private const int Workers = 4;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => ForwardAsync(stoppingToken)));

    private async Task ForwardAsync(CancellationToken stoppingToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        await foreach (var click in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var response = await http.PostAsJsonAsync("api/clicks", click, AppJsonContext.Default.RecordClickRequest, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Analytics rejected click for {Code}: {Status}", click.Code, (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Dropped click for {Code}: {Error}", click.Code, ex.Message);
            }
        }
    }
}
