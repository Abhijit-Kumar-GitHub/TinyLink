using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace TinyLink.ServiceDefaults;

public static class TinyLinkMetrics
{
    public const string MeterName = "TinyLink";

    /// <summary>Shared meter for the services' own instruments (exported as tinylink_*).</summary>
    public static readonly Meter Meter = new(MeterName);

    // Default ASP.NET Core buckets start at 5 ms; in-cluster redirects take ~1 ms, so add finer ones.
    private static readonly double[] LatencyBuckets = [0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    /// <summary>OpenTelemetry metrics (ASP.NET Core, Kestrel, HttpClient, runtime, TinyLink) exported for Prometheus.</summary>
    public static IServiceCollection AddTinyLinkMetrics(this IServiceCollection services)
    {
        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(
                "Microsoft.AspNetCore.Hosting",
                "Microsoft.AspNetCore.Server.Kestrel",
                "System.Net.Http",
                "System.Runtime",
                MeterName)
            .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = LatencyBuckets })
            .AddView("http.client.request.duration", new ExplicitBucketHistogramConfiguration { Boundaries = LatencyBuckets })
            .AddPrometheusExporter());
        return services;
    }

    /// <summary>
    /// Serves /metrics. When Metrics:Port is set (in Kubernetes) the endpoint answers only on that port,
    /// which the Service/Ingress never expose, so visitors on the public port cannot read it.
    /// </summary>
    public static IEndpointRouteBuilder MapTinyLinkMetrics(this IEndpointRouteBuilder endpoints, IConfiguration config)
    {
        var endpoint = endpoints.MapPrometheusScrapingEndpoint("/metrics");
        if (config["Metrics:Port"] is { Length: > 0 } port)
        {
            endpoint.RequireHost($"*:{port}");
        }

        return endpoints;
    }
}
