using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.Ordering.Commands.CancelOrder;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

public class CancelOrderTests
{
    private readonly ApplicationTestHost _host = new();
    private readonly Guid _ayse = Guid.NewGuid();

    public CancelOrderTests()
    {
        _host.CurrentUser.UserId = _ayse;
    }

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
    public async Task ConfirmedOrder_Cancelled_ItsReservedStockGoesBackToTheShelf()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 3);
        await _host.DispatchDomainEventsAsync();                // outbox: reserve → 7, Confirmed
        Assert.Equal((OrderStatus.Confirmed, 7), (order.Status, phone.StockQuantity));

        await _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "changed my mind"));
        Assert.Equal(7, phone.StockQuantity);                   // the request itself never touches Product
        await _host.DispatchDomainEventsAsync();                // outbox: release

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, phone.StockQuantity);
    }

    [Fact]
    public async Task CancelledBeforeTheOutboxRan_ReservationIsSkipped_StockNeverMoves()
    {
        var phone = Seed(stock: 10);
        var order = await PlaceAsync(phone, 3);

        await _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "too slow")); // OrderPlaced still pending
        await _host.DispatchDomainEventsAsync();   // OrderPlaced: finds Cancelled → skip; OrderCancelled: nothing to release

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(10, phone.StockQuantity);     // neither -3 nor +3: no phantom stock
    }

    [Fact]
    public async Task SystemCancellation_ForMissingStock_ReturnsNothing()
    {
        var phone = Seed(stock: 5);
        var order = await PlaceAsync(phone, 3);
        phone.RemoveStock(Quantity.Create(4));     // someone else bought first: 1 left
        phone.ClearDomainEvents();

        await _host.DispatchDomainEventsAsync();   // reservation cancels: insufficient stock
        await _host.DispatchDomainEventsAsync();   // OrderCancelled delivered

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(1, phone.StockQuantity);      // still 1, not 4
    }

    [Fact]
    public async Task SomeoneElsesOrder_IsNotFound()
    {
        var order = await PlaceAsync(Seed(stock: 5), 1);
        _host.CurrentUser.UserId = Guid.NewGuid(); // Mehmet

        await Assert.ThrowsAsync<NotFoundException>(() => _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "ha ha")));
        Assert.Equal(OrderStatus.Placed, order.Status);
    }

    [Fact]
    public async Task CancellingTwice_IsABusinessRuleViolation()
    {
        var order = await PlaceAsync(Seed(stock: 5), 1);
        await _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "first"));

        await Assert.ThrowsAsync<DomainException>(() => _host.Mediator.Send(new CancelOrderCommand(order.Id.Value, "second")));
    }

    [Fact]
    public async Task MissingReason_IsAValidationError()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _host.Mediator.Send(new CancelOrderCommand(Guid.NewGuid(), "")));

        Assert.Contains(nameof(CancelOrderCommand.Reason), ex.Errors.Keys);
    }
}
