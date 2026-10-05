using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TinyLink.ServiceDefaults;

// One log line per request with status and duration. The trace id comes from W3C traceparent, which
// ASP.NET Core and HttpClient propagate automatically, so a redirect -> link lookup shares one id.
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public const string TraceIdHeader = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TraceIdHeader] = traceId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            // Kubernetes probes and Prometheus hit these every few seconds; keep them out of normal logs.
            var level = context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/metrics")
                ? LogLevel.Debug
                : LogLevel.Information;
            logger.Log(level, "HTTP {Method} {Path} -> {StatusCode} in {ElapsedMs:0.0} ms (trace {TraceId})",
                context.Request.Method, context.Request.Path.Value, context.Response.StatusCode,
                Stopwatch.GetElapsedTime(start).TotalMilliseconds, traceId);
        }
    }
}
