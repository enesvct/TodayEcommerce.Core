using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository : IProductRepository
{
    private readonly AppDbContext _dbContext;

    public ProductRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default) =>
        _dbContext.Products.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default)
    {
        // Translated to a single "WHERE id = ANY(@ids)" query.
        return await _dbContext.Products
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public void Add(Product product) => _dbContext.Products.Add(product);
}
