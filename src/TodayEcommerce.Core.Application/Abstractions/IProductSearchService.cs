using TodayEcommerce.Core.Application.Catalog.Search;

namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Full-text product search. Implemented with Elasticsearch in Infrastructure.
/// </summary>
public interface IProductSearchService
{
    /// <summary>
    /// Adds or replaces the document. Must not throw on search-engine failures (logs instead):
    /// the product is already saved, and a stale index entry is repaired by the next write or a reindex.
    /// </summary>
    Task IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default);

    /// <summary>Throws <c>ServiceUnavailableException</c> when the search engine can't be reached.</summary>
    Task<PagedResult<ProductSearchHit>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default);
}
