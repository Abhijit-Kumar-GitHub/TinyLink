using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TinyLink.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    public const string ReadyTag = "ready";

    /// <summary>ProblemDetails, the global exception handler, and health checks for the JSON APIs.</summary>
    public static IServiceCollection AddApiDefaults(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHealthChecks();

        // EF's retrying execution strategy can stall a DB check for 30s+; probes must answer fast.
        services.PostConfigure<HealthCheckServiceOptions>(o =>
        {
            foreach (var registration in o.Registrations.Where(r => r.Timeout == Timeout.InfiniteTimeSpan))
            {
                registration.Timeout = TimeSpan.FromSeconds(3);
            }
        });
        return services;
    }

    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app) =>
        app.UseMiddleware<RequestLoggingMiddleware>();

    /// <summary>/health/live runs no checks (process is up); /health/ready runs checks tagged "ready".</summary>
    public static IEndpointRouteBuilder MapDefaultHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
        return endpoints;
    }
}
