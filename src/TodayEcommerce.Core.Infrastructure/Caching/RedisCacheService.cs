using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;

namespace TodayEcommerce.Core.Infrastructure.Caching;

/// <summary>
/// Redis-backed cache via IDistributedCache. Fails open: if Redis is down or slow,
/// errors are logged and treated as a cache miss, so requests fall back to the database
/// instead of failing. A cache is an optimisation, never a dependency the app can't live without.
/// </summary>
internal sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache read failed for {CacheKey}; falling back to source", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await _cache.SetAsync(
                key,
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write failed for {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Can't invalidate → the entry lives until its TTL expires (the safety net).
            _logger.LogWarning(ex, "Cache invalidation failed for {CacheKey}; entry expires by TTL", key);
        }
    }
}
