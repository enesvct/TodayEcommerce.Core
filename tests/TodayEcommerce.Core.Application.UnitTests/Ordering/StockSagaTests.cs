using TodayEcommerce.Core.Application.Catalog.Contracts;
using TodayEcommerce.Core.Application.Ordering.Commands.CancelOrder;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.Ordering.Contracts;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

/// <summary>Ordering and Catalog talking only through their contracts — including the awkward orderings.</summary>
public class StockSagaTests
{
    private readonly ApplicationTestHost _host = new();

    private Product Seed(int stock)
    {
        var product = Product.Create("Telefon", Money.Create(1_000m, "TRY"), stock);
        product.ClearDomainEvents();
        _host.Products.Seed(product);
        return product;
    }

    private async Task<Order> PlaceAsync(Product product, int quantity)
    {
        var id = await _host.Mediator.Send(new CreateOrderCommand(new[] { new CreateOrderItem(product.Id.Value, quantity) }));
        return _host.Orders.Added.Single(o => o.Id.Value == id);
    }

    [Fact]
    public async Task HappyPath_TheCatalogRecordsWhatItReserved_AndOrderingConfirms()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 3);

        await _host.DispatchDomainEventsAsync();

        var reservation = Assert.Single(_host.Reservations.All);
        Assert.Equal(order.Id.Value, reservation.OrderId);
        Assert.True(reservation.IsActive);
        Assert.Equal((OrderStatus.Confirmed, 7), (order.Status, phone.StockQuantity));
        Assert.Contains(_host.IntegrationEvents.Published, e => e is StockReservedIntegrationEvent);
    }

    [Fact]
    public async Task MessagesCrossed_CancelArrivedBeforeTheReservation_OrderingCompensates()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 3);
        await _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "vazgectim"));
        order.ClearDomainEvents();          // we deliver the messages by hand, in the "wrong" order:

        // 1. OrderCancelled reaches the Catalog first (OrderPlaced was stuck in back-off): nothing to release yet.
        await _host.DispatchAsync([new OrderCancelledIntegrationEvent(order.Id.Value)]);
        Assert.Equal(10, phone.StockQuantity);

        // 2. The retried OrderPlaced arrives: the Catalog, knowing nothing, reserves 3 units for a cancelled order.
        //    Its StockReserved reaches Ordering, which sees "Cancelled" and asks for the reservation to be undone.
        await _host.DispatchAsync([new OrderPlacedIntegrationEvent(order.Id.Value, [new OrderedProduct(phone.Id.Value, 3)])]);

        Assert.Contains(_host.IntegrationEvents.Published, e => e is ReservationNoLongerNeededIntegrationEvent);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, phone.StockQuantity);                               // the 3 units are back
        Assert.False(Assert.Single(_host.Reservations.All).IsActive);        // released, with a record of why
    }

    [Fact]
    public async Task RedeliveredOrderPlaced_ReservesOnce()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 2);
        await _host.DispatchDomainEventsAsync();
        var placed = new OrderPlacedIntegrationEvent(order.Id.Value, [new OrderedProduct(phone.Id.Value, 2)]);

        await _host.DispatchAsync([placed, placed]); // at-least-once: the same message twice more

        Assert.Equal(8, phone.StockQuantity);
        Assert.Single(_host.Reservations.All);
    }

    [Fact]
    public async Task RedeliveredCancellation_ReleasesOnce()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 2);
        await _host.DispatchDomainEventsAsync();
        await _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "vazgectim"));
        await _host.DispatchDomainEventsAsync();
        Assert.Equal(10, phone.StockQuantity);

        await _host.DispatchAsync([new OrderCancelledIntegrationEvent(order.Id.Value)]);

        Assert.Equal(10, phone.StockQuantity); // not 12
    }

    [Fact]
    public async Task FailedReservation_LeavesNoReservation_AndTheCancellationReleasesNothing()
    {
        var phone = Seed(stock: 1);
        var order = await PlaceAsync(phone, 1);
        phone.RemoveStock(Quantity.Create(1)); // sold out meanwhile
        phone.ClearDomainEvents();

        await _host.DispatchDomainEventsAsync();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Empty(_host.Reservations.All);
        Assert.Equal(0, phone.StockQuantity);
        Assert.Contains(_host.IntegrationEvents.Published, e => e is StockReservationFailedIntegrationEvent);
    }
}
