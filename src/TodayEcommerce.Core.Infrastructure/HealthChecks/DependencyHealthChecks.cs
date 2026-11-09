using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodayEcommerce.Core.Infrastructure.Persistence;
using TodayEcommerce.Core.Infrastructure.Search;

namespace TodayEcommerce.Core.Infrastructure.HealthChecks;

// Why every check ends with .WaitAsync(cancellationToken):
// the registration timeout only CANCELS the token; the health check service still awaits the check.
// Clients that ignore the token while connecting (Redis, a TCP connect to a dead host) would keep the probe
// hanging for 15+ seconds — long enough for the platform to declare the instance dead. WaitAsync stops
// waiting the moment the timeout fires, and the service reports the check's failure status ("timed out").

/// <summary>Tag of the checks that decide whether this instance may receive traffic (/health/ready).</summary>
public static class HealthCheckTags
{
    public const string Ready = "ready";
}

/// <summary>Can we open a connection and run a query? Without the database nothing works.</summary>
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public PostgresHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await _db.Database.CanConnectAsync(cancellationToken).WaitAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus);
}

/// <summary>Round-trip through the same cache client the app uses.</summary>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IDistributedCache _cache;

    public RedisHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // WaitAsync: the Redis client does not honour the token while it is still connecting.
            await _cache.GetAsync("health:probe", cancellationToken).WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}

/// <summary>Cluster status via the same HttpClient the search adapter uses. Yellow is normal on a single node.</summary>
internal sealed class ElasticsearchHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ElasticsearchHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(ProductIndex.HttpClientName);
            using var response = await client.GetAsync("_cluster/health", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return response.IsSuccessStatusCode && !body.Contains("\"status\":\"red\"")
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(context.Registration.FailureStatus, "cluster not usable");
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }
}
