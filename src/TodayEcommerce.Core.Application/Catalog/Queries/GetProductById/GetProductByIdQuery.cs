using MediatR;
using TodayEcommerce.Core.Application.Common.Caching;

namespace TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;

/// <summary>
/// Read-only request: returns the product, or null when it does not exist.
/// Changes nothing in the system (CQRS query side). Served from the cache when possible.
/// </summary>
/// <remarks>
/// Cached data may be up to <see cref="Expiration"/> old (e.g. stock level). That is acceptable for
/// display; placing an order always re-reads the product from the database.
/// </remarks>
public sealed record GetProductByIdQuery(Guid ProductId) : IRequest<ProductDto?>, ICacheableQuery
{
    public string CacheKey => CacheKeys.Product(ProductId);

    public TimeSpan Expiration => TimeSpan.FromMinutes(5);
}

/// <summary>
/// What the outside world sees. A flat, serializable shape — never the Product aggregate itself,
/// so the domain model can evolve without breaking API clients.
/// </summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool InStock);
