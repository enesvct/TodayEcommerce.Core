using MediatR;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Caching;

namespace TodayEcommerce.Core.Application.Common.Behaviors;

/// <summary>
/// Cache-aside for every <see cref="ICacheableQuery"/>: return the cached result if present;
/// otherwise run the handler, store its result, and return it. Other requests pass straight through.
/// </summary>
internal sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICacheService _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(ICacheService cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheableQuery cacheable)
        {
            return await next();
        }

        var cached = await _cache.GetAsync<CacheEntry>(cacheable.CacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogInformation("Cache HIT {CacheKey}", cacheable.CacheKey);
            return cached.Value;
        }

        _logger.LogInformation("Cache MISS {CacheKey}", cacheable.CacheKey);
        var response = await next();

        // "Not found" is not cached: a product created a moment later must be visible immediately.
        if (response is not null)
        {
            await _cache.SetAsync(cacheable.CacheKey, new CacheEntry(response), cacheable.Expiration, cancellationToken);
        }

        return response;
    }

    /// <summary>Wrapper so TResponse can be any type (the cache API requires a reference type).</summary>
    private sealed record CacheEntry(TResponse Value);
}
