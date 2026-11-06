namespace TodayEcommerce.Core.Application.Common.Caching;

/// <summary>
/// Marks a query whose result may be served from the cache (cache-aside via CachingBehavior).
/// Only queries — never commands — may implement this: writes must always see the source of truth.
/// </summary>
public interface ICacheableQuery
{
    string CacheKey { get; }

    /// <summary>Safety net: the entry disappears after this even if an invalidation is missed.</summary>
    TimeSpan Expiration { get; }
}
