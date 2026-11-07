using TodayEcommerce.Core.Application.Catalog.Commands.ChangeProductPrice;
using TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;
using TodayEcommerce.Core.Application.Common.Caching;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Catalog;

public class ProductCachingTests
{
    private readonly ApplicationTestHost _host = new();

    private Product SeedPhone(decimal price = 10_000m, int stock = 5)
    {
        var product = Product.Create("Telefon X", Money.Create(price, "TRY"), stock);
        _host.Products.Seed(product);
        return product;
    }

    [Fact]
    public async Task SecondRead_IsServedFromCache_WithoutQueryingTheDatabase()
    {
        var phone = SeedPhone();
        var query = new GetProductByIdQuery(phone.Id.Value);

        var first = await _host.Mediator.Send(query);   // MISS → database → cache
        var second = await _host.Mediator.Send(query);  // HIT  → cache only

        Assert.Equal(1, _host.Products.QueryCount);
        Assert.Equal(first, second); // same data after a JSON round-trip
        Assert.Contains(CacheKeys.Product(phone.Id.Value), _host.Cache.Keys);
    }

    [Fact]
    public async Task NotFound_IsNotCached_SoANewProductBecomesVisibleImmediately()
    {
        var id = Guid.NewGuid();

        Assert.Null(await _host.Mediator.Send(new GetProductByIdQuery(id)));

        Assert.Empty(_host.Cache.Keys);
    }

    [Fact]
    public async Task ChangingPrice_InvalidatesCache_SoNextReadShowsNewPrice()
    {
        var phone = SeedPhone(price: 10_000m);
        await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value)); // cached at 10.000

        await _host.Mediator.Send(new ChangeProductPriceCommand(phone.Id.Value, 12_000m));
        await _host.DispatchDomainEventsAsync(); // the price-changed event's handler evicts the entry
        var afterChange = await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value));

        Assert.Equal(12_000m, afterChange!.Price);
    }

    [Fact]
    public async Task UntilTheEventIsHandled_CacheStillServesTheOldPrice_EventualConsistency()
    {
        // The honest trade-off of moving invalidation to the outbox: a short window of staleness.
        var phone = SeedPhone(price: 10_000m);
        await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value));

        await _host.Mediator.Send(new ChangeProductPriceCommand(phone.Id.Value, 12_000m));
        var beforeDispatch = await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value));

        Assert.Equal(10_000m, beforeDispatch!.Price);
    }

    [Fact]
    public async Task WithoutInvalidation_CacheWouldServeStaleData()
    {
        // Documents WHY invalidation matters: change the aggregate behind the cache's back.
        var phone = SeedPhone(price: 10_000m);
        await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value));

        phone.ChangePrice(Money.Create(12_000m, "TRY")); // no command → no invalidation
        var stale = await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value));

        Assert.Equal(10_000m, stale!.Price);
    }

    [Fact]
    public async Task Commands_NeverUseTheCache_TheyReadTheSourceOfTruth()
    {
        var phone = SeedPhone(stock: 5);
        await _host.Mediator.Send(new GetProductByIdQuery(phone.Id.Value)); // cache says stock = 5
        phone.RemoveStock(Quantity.Create(5));                              // real stock is now 0
        var queriesBefore = _host.Products.QueryCount;

        var order = new CreateOrderCommand(Guid.NewGuid(), new[] { new CreateOrderItem(phone.Id.Value, 1) });

        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => _host.Mediator.Send(order));
        Assert.Equal(queriesBefore + 1, _host.Products.QueryCount); // went to the database, saw stock 0
    }
}
