using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.UnitTests.Fakes;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.UnitTests.Ordering;

public class IdempotentCreateOrderTests
{
    private readonly ApplicationTestHost _host = new();

    private Product Seed(int stock = 10)
    {
        var product = Product.Create("Telefon", Money.Create(1_000m, "TRY"), stock);
        _host.Products.Seed(product);
        return product;
    }

    private static CreateOrderCommand Order(Product product, int quantity = 1, string? key = null) =>
        new(new[] { new CreateOrderItem(product.Id.Value, quantity) }, key);

    [Fact]
    public async Task Retry_WithTheSameKey_ReturnsTheOriginalOrder_AndPlacesNoSecondOne()
    {
        var phone = Seed();

        var first = await _host.Mediator.Send(Order(phone, key: "checkout-1"));
        var retry = await _host.Mediator.Send(Order(phone, key: "checkout-1"));

        Assert.Equal(first, retry);
        Assert.Single(_host.Orders.Added);
        Assert.Equal(1, _host.UnitOfWork.SaveCount); // the retry did not even write
    }

    [Fact]
    public async Task Retry_AfterTheFirstOrderTookTheLastUnit_StillSucceeds_NotOutOfStock()
    {
        var phone = Seed(stock: 1);
        var first = await _host.Mediator.Send(Order(phone, key: "checkout-1"));
        phone.RemoveStock(Quantity.Create(1)); // the outbox reserved the last unit for that order

        var retry = await _host.Mediator.Send(Order(phone, key: "checkout-1"));

        Assert.Equal(first, retry); // the client gets "your order", not a misleading 422 out-of-stock
    }

    [Fact]
    public async Task SameKey_ForADifferentRequest_IsRejected()
    {
        var phone = Seed();
        await _host.Mediator.Send(Order(phone, quantity: 1, key: "checkout-1"));

        await Assert.ThrowsAsync<IdempotencyKeyReusedException>(
            () => _host.Mediator.Send(Order(phone, quantity: 3, key: "checkout-1")));
        Assert.Single(_host.Orders.Added);
    }

    [Fact]
    public async Task NewKey_IsANewIntention_AndPlacesASecondOrder()
    {
        var phone = Seed();

        var first = await _host.Mediator.Send(Order(phone, key: "checkout-1"));
        var second = await _host.Mediator.Send(Order(phone, key: "checkout-2"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, _host.Orders.Added.Count);
    }

    [Fact]
    public async Task Keys_ArePerUser_AyseCannotReceiveMehmetsOrder()
    {
        var phone = Seed();
        _host.CurrentUser.UserId = Guid.NewGuid(); // Mehmet
        var mehmets = await _host.Mediator.Send(Order(phone, key: "checkout-1"));

        _host.CurrentUser.UserId = Guid.NewGuid(); // Ayşe, by chance the same key
        var ayses = await _host.Mediator.Send(Order(phone, key: "checkout-1"));

        Assert.NotEqual(mehmets, ayses);
        Assert.Equal(2, _host.Orders.Added.Count);
    }

    [Fact]
    public async Task WithoutAKey_BehavesAsBefore_EveryRequestIsAnOrder()
    {
        var phone = Seed();

        await _host.Mediator.Send(Order(phone));
        await _host.Mediator.Send(Order(phone));

        Assert.Equal(2, _host.Orders.Added.Count);
        Assert.Equal(0, _host.Idempotency.Count);
    }

    [Fact]
    public async Task TooLongKey_IsAValidationError() // limit: 100 characters
    {
        var phone = Seed();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _host.Mediator.Send(Order(phone, key: new string('k', 101))));

        Assert.Contains(nameof(CreateOrderCommand.IdempotencyKey), ex.Errors.Keys);
    }
}
