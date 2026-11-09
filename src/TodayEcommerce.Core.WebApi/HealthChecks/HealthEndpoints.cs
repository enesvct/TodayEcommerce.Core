using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodayEcommerce.Core.Infrastructure.HealthChecks;

namespace TodayEcommerce.Core.WebApi.HealthChecks;

/// <summary>
/// Probes for the platform (load balancer, Kubernetes). Anonymous on purpose: the prober has no token.
/// </summary>
public static class HealthEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness: "is the process alive?" Checks NO dependency: restarting the container because the
        // database is down would not fix the database, it would only add a restart loop.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = _ => false,
                ResponseWriter = WriteAsync,
            })
            .AllowAnonymous();

        // Readiness: "may I receive traffic?" Healthy/Degraded → 200 (keep serving), Unhealthy → 503.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(HealthCheckTags.Ready),
                ResponseWriter = WriteAsync,
            })
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Names, states and timings only. No exception messages or connection details: this endpoint is public.
    /// </summary>
    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var body = new
        {
            Status = report.Status.ToString(),
            DurationMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            Checks = report.Entries.Select(e => new
            {
                Name = e.Key,
                Status = e.Value.Status.ToString(),
                DurationMs = Math.Round(e.Value.Duration.TotalMilliseconds),
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
