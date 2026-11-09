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

    public IReadOnlyCollection<Product> All => _products.Values;

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

/// <summary>
/// Stores JSON (like Redis does), so a DTO that can't round-trip through serialization fails here too.
/// </summary>
public sealed class FakeCacheService : ICacheService
{
    private readonly Dictionary<string, string> _entries = new();

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class =>
        Task.FromResult(_entries.TryGetValue(key, out var json) ? System.Text.Json.JsonSerializer.Deserialize<T>(json) : null);

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        where T : class
    {
        _entries[key] = System.Text.Json.JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class FakeProductSearchService : IProductSearchService
{
    public Dictionary<Guid, Application.Catalog.Search.ProductSearchDocument> Indexed { get; } = new();
    public Application.Catalog.Search.ProductSearchCriteria? LastCriteria { get; private set; }

    public Task IndexAsync(Application.Catalog.Search.ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        Indexed[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task<Application.Catalog.Search.PagedResult<Application.Catalog.Search.ProductSearchHit>> SearchAsync(
        Application.Catalog.Search.ProductSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        LastCriteria = criteria;
        return Task.FromResult(new Application.Catalog.Search.PagedResult<Application.Catalog.Search.ProductSearchHit>(
            Array.Empty<Application.Catalog.Search.ProductSearchHit>(), 0, criteria.Page, criteria.PageSize));
    }
}

public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
}

/// <summary>The "signed-in" user of a test; switch <see cref="UserId"/> to act as someone else.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid UserId { get; set; } = Guid.NewGuid();
}
