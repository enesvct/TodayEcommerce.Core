namespace TodayEcommerce.Core.Application.Common.Caching;

/// <summary>
/// The one place cache keys are built, so the query that writes a key and the command that
/// invalidates it can never drift apart. The "v1" prefix lets us change the cached shape (DTO)
/// later without reading old, incompatible entries — just bump the version.
/// </summary>
public static class CacheKeys
{
    private const string Version = "v1";

    public static string Product(Guid productId) => $"{Version}:products:{productId}";
}
