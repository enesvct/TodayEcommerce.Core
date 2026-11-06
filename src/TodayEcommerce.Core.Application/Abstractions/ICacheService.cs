namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Key/value cache used only by the read side. Implemented with Redis in Infrastructure.
/// Implementations must never throw for cache failures: a broken cache is treated as a miss,
/// so the application keeps working (slower) from the database.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        where T : class;

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}
