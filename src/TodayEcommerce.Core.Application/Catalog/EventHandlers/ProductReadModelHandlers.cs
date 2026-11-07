using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Application.Common.Caching;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Catalog.Events;

namespace TodayEcommerce.Core.Application.Catalog.EventHandlers;

/// <summary>
/// Keeps the search index in sync with the database. Replaces the manual IndexAsync calls that used to
/// live in every command handler — a new command that changes a product can no longer "forget" this.
/// </summary>
/// <remarks>
/// Idempotent: it re-reads the CURRENT product and overwrites the document by id, so handling the same
/// (or an older, out-of-order) event again simply writes the latest state once more.
/// </remarks>
internal sealed class SyncProductSearchIndexHandler :
    IDomainEventHandler<ProductCreatedDomainEvent>,
    IDomainEventHandler<ProductRenamedDomainEvent>,
    IDomainEventHandler<ProductPriceChangedDomainEvent>,
    IDomainEventHandler<ProductStockChangedDomainEvent>
{
    private readonly IProductRepository _products;
    private readonly IProductSearchService _search;

    public SyncProductSearchIndexHandler(IProductRepository products, IProductSearchService search)
    {
        _products = products;
        _search = search;
    }

    public Task HandleAsync(ProductCreatedDomainEvent e, CancellationToken ct) => SyncAsync(e, ct);
    public Task HandleAsync(ProductRenamedDomainEvent e, CancellationToken ct) => SyncAsync(e, ct);
    public Task HandleAsync(ProductPriceChangedDomainEvent e, CancellationToken ct) => SyncAsync(e, ct);
    public Task HandleAsync(ProductStockChangedDomainEvent e, CancellationToken ct) => SyncAsync(e, ct);

    private async Task SyncAsync(IProductChangedEvent e, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(e.ProductId, cancellationToken);
        if (product is null)
        {
            return; // nothing to index (product no longer exists)
        }

        // Throws if Elasticsearch is down → the outbox keeps the message and retries later.
        await _search.IndexAsync(ProductSearchDocument.From(product), cancellationToken);
    }
}

/// <summary>Evicts the cached product so the next read loads fresh data. Deleting twice is harmless (idempotent).</summary>
internal sealed class InvalidateProductCacheHandler :
    IDomainEventHandler<ProductRenamedDomainEvent>,
    IDomainEventHandler<ProductPriceChangedDomainEvent>,
    IDomainEventHandler<ProductStockChangedDomainEvent>
{
    private readonly ICacheService _cache;

    public InvalidateProductCacheHandler(ICacheService cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(ProductRenamedDomainEvent e, CancellationToken ct) => EvictAsync(e, ct);
    public Task HandleAsync(ProductPriceChangedDomainEvent e, CancellationToken ct) => EvictAsync(e, ct);
    public Task HandleAsync(ProductStockChangedDomainEvent e, CancellationToken ct) => EvictAsync(e, ct);

    private Task EvictAsync(IProductChangedEvent e, CancellationToken cancellationToken) =>
        _cache.RemoveAsync(CacheKeys.Product(e.ProductId.Value), cancellationToken);
}
