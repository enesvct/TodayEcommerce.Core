using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Application.Catalog.Search;

/// <summary>
/// The searchable copy of a product (a read model). Derived from the Product aggregate;
/// the database stays the source of truth and the index can always be rebuilt from it.
/// </summary>
public sealed record ProductSearchDocument(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool InStock)
{
    public static ProductSearchDocument From(Product product) => new(
        product.Id.Value,
        product.Name,
        product.Price.Amount,
        product.Price.Currency,
        product.StockQuantity,
        product.StockQuantity > 0);
}

public sealed record ProductSearchCriteria(
    string? Text,
    decimal? MinPrice,
    decimal? MaxPrice,
    bool InStockOnly,
    int Page,
    int PageSize);

public sealed record ProductSearchHit(ProductSearchDocument Product, double? Score);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);
