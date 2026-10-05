using System.Text.RegularExpressions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using RedirectService;
using TinyLink.Contracts;
using TinyLink.ServiceDefaults;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddApiDefaults();
builder.Services.AddTinyLinkMetrics();

builder.Services.AddHttpClient<LinkLookupClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:LinkService"]!))
    .AddResilienceHandler("link-lookup", pipeline =>
    {
        pipeline.AddTimeout(TimeSpan.FromSeconds(3));
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(10),
            BreakDuration = TimeSpan.FromSeconds(15),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(1));
    });

builder.Services.AddSingleton<ClickQueue>();
builder.Services.AddHostedService<ClickForwarder>();
builder.Services.AddHttpClient(ClickForwarder.HttpClientName, c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Services:AnalyticsService"]!);
    c.Timeout = TimeSpan.FromSeconds(2);
});

var app = builder.Build();

app.UseRequestLogging();
app.UseExceptionHandler();
app.MapDefaultHealthChecks();
app.MapTinyLinkMetrics(app.Configuration);

var codePattern = new Regex("^[A-Za-z0-9_-]{1,16}$", RegexOptions.Compiled);

app.MapGet("/{code}", async (string code, HttpContext http, LinkLookupClient links, ClickQueue clicks, ILogger<Program> logger, CancellationToken ct) =>
{
    if (!codePattern.IsMatch(code))
    {
        return Results.NotFound();
    }

    string? url;
    try
    {
        url = await links.ResolveAsync(code, ct);
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        logger.LogWarning("Link lookup failed for {Code}: {Error}", code, ex.Message);
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    if (url is null)
    {
        return Results.NotFound();
    }

    clicks.Enqueue(new RecordClickRequest(
        code,
        DateTime.UtcNow,
        http.Request.Headers.Referer.ToString() is { Length: > 0 } referrer ? referrer : null,
        http.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null));

    return Results.Redirect(url);
});

app.Run();
