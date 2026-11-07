using TodayEcommerce.Core.Application.Catalog.Search;

namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Full-text product search. Implemented with Elasticsearch in Infrastructure.
/// </summary>
public interface IProductSearchService
{
    /// <summary>
    /// Adds or replaces the document (idempotent: same id overwrites). Throws <c>ServiceUnavailableException</c>
    /// when the search engine can't be reached, so the calling outbox handler fails and the event is retried.
    /// (It used to swallow errors — acceptable only while nothing could retry.)
    /// </summary>
    Task IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default);

    /// <summary>Throws <c>ServiceUnavailableException</c> when the search engine can't be reached.</summary>
    Task<PagedResult<ProductSearchHit>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default);
}
