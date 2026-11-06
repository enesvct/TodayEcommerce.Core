using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Application.UnitTests.Fakes;

/// <summary>
/// Hand-written in-memory stand-ins for Infrastructure. They also record how they were used,
/// so tests can prove e.g. that the database was never touched for an invalid request.
/// </summary>
public sealed class FakeProductRepository : IProductRepository
{
    private readonly Dictionary<ProductId, Product> _products = new();

    public int QueryCount { get; private set; }

    public void Seed(params Product[] products)
    {
        foreach (var product in products)
        {
            _products[product.Id] = product;
        }
    }

    public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
    {
        QueryCount++;
        return Task.FromResult(_products.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<Product>> GetByIdsAsync(
        IReadOnlyCollection<ProductId> ids,
        CancellationToken cancellationToken = default)
    {
        QueryCount++;
        IReadOnlyList<Product> found = ids.Where(_products.ContainsKey).Select(id => _products[id]).ToList();
        return Task.FromResult(found);
    }

    public void Add(Product product) => _products[product.Id] = product;
}

public sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Added { get; } = new();

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Added.SingleOrDefault(o => o.Id == id));

    public void Add(Order order) => Added.Add(order);
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
}
