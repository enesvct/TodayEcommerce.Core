namespace TodayEcommerce.Core.Domain.Catalog;

/// <summary>
/// Persistence contract for the Product aggregate. Declared in Domain (what we need),
/// implemented in Infrastructure (how it is done) — Dependency Inversion.
/// </summary>
/// <remarks>
/// Works with whole aggregates only: there is no "UpdateStock(id, value)" method,
/// because every change must go through Product's own methods and rules.
/// </remarks>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default);

    void Add(Product product);
}
