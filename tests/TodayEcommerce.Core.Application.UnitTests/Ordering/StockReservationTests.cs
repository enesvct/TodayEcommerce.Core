using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

public class StockReservationTests
{
    private readonly ApplicationTestHost _host = new();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private Product Seed(string name, int stock)
    {
        var product = Product.Create(name, Money.Create(100m, "TRY"), stock);
        product.ClearDomainEvents();
        _host.Products.Seed(product);
        return product;
    }

    private async Task<Order> PlaceAsync(params (Product Product, int Quantity)[] lines)
    {
        var id = await _host.Mediator.Send(new CreateOrderCommand(
            CustomerId,
            lines.Select(l => new CreateOrderItem(l.Product.Id.Value, l.Quantity)).ToList()));
        return _host.Orders.Added.Single(o => o.Id.Value == id);
    }

    [Fact]
    public async Task AllLinesAvailable_DecreasesEveryStock_AndConfirms()
    {
        var phone = Seed("Telefon", 5);
        var @case = Seed("Kilif", 5);
        var order = await PlaceAsync((phone, 2), (@case, 1));

        await _host.DispatchDomainEventsAsync();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(3, phone.StockQuantity);
        Assert.Equal(4, @case.StockQuantity);
    }

    [Fact]
    public async Task OneLineShort_CancelsOrder_AndTouchesNoStock_AllOrNothing()
    {
        var phone = Seed("Telefon", 5);
        var @case = Seed("Kilif", 1);
        var order = await PlaceAsync((phone, 2), (@case, 1));
        @case.RemoveStock(Quantity.Create(1)); // someone else bought the last case meanwhile
        @case.ClearDomainEvents();

        await _host.DispatchDomainEventsAsync();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains("Kilif", order.CancellationReason);
        Assert.Equal(5, phone.StockQuantity); // NOT 3: nothing was reserved
    }

    [Fact]
    public async Task LastUnit_TwoOrders_FirstIsConfirmed_SecondIsCancelled()
    {
        var phone = Seed("Telefon", 1);
        var ayse = await PlaceAsync((phone, 1));   // both pass the early check (stock 1)
        var mehmet = await PlaceAsync((phone, 1));

        await _host.DispatchDomainEventsAsync();

        Assert.Equal(OrderStatus.Confirmed, ayse.Status);
        Assert.Equal(OrderStatus.Cancelled, mehmet.Status);
        Assert.Equal(0, phone.StockQuantity);
    }

    [Fact]
    public async Task RedeliveredOrderPlacedEvent_DoesNotDecreaseStockTwice()
    {
        var phone = Seed("Telefon", 10);
        var order = await PlaceAsync((phone, 2));
        var placedEvent = order.DomainEvents.ToList();

        await _host.DispatchDomainEventsAsync();
        await _host.DispatchAsync(placedEvent); // at-least-once: the same event again
        await _host.DispatchAsync(placedEvent); // ...and again

        Assert.Equal(8, phone.StockQuantity);   // decreased once, not 3 times
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public async Task ReservingStock_RaisesStockChanged_SoSearchIndexGetsTheNewLevel()
    {
        var phone = Seed("Telefon", 1);
        await PlaceAsync((phone, 1));

        await _host.DispatchDomainEventsAsync(); // OrderPlaced → stock 0 (raises ProductStockChanged)
        await _host.DispatchDomainEventsAsync(); // ProductStockChanged → index

        var indexed = _host.Search.Indexed[phone.Id.Value];
        Assert.Equal(0, indexed.StockQuantity);
        Assert.False(indexed.InStock);
    }
}
