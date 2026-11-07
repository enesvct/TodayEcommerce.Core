using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Catalog.Events;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.Ordering.Events;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.UnitTests;

public class DomainEventTests
{
    private static Money Try(decimal amount) => Money.Create(amount, "TRY");

    private static Product NewPhone(int stock = 5)
    {
        var product = Product.Create("Telefon X", Try(10_000m), stock);
        product.ClearDomainEvents(); // isolate each test to the action under test
        return product;
    }

    [Fact]
    public void CreatingProduct_RaisesProductCreated()
    {
        var product = Product.Create("Telefon X", Try(10_000m), 5);

        var e = Assert.IsType<ProductCreatedDomainEvent>(Assert.Single(product.DomainEvents));
        Assert.Equal(product.Id, e.ProductId);
    }

    [Fact]
    public void ChangingPrice_RaisesEventWithOldAndNewAmount()
    {
        var product = NewPhone();

        product.ChangePrice(Try(12_000m));

        var e = Assert.IsType<ProductPriceChangedDomainEvent>(Assert.Single(product.DomainEvents));
        Assert.Equal(10_000m, e.OldAmount);
        Assert.Equal(12_000m, e.NewAmount);
    }

    [Fact]
    public void SettingTheSamePrice_RaisesNothing_BecauseNothingHappened()
    {
        var product = NewPhone();

        product.ChangePrice(Try(10_000m));
        product.Rename("Telefon X");

        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void StockChanges_RaiseStockChangedWithNewLevel()
    {
        var product = NewPhone(stock: 5);

        product.RemoveStock(Quantity.Create(2));
        product.AddStock(Quantity.Create(10));

        var levels = product.DomainEvents.Cast<ProductStockChangedDomainEvent>().Select(e => e.NewStockQuantity);
        Assert.Equal(new[] { 3, 13 }, levels);
    }

    [Fact]
    public void FailedRemoveStock_RaisesNothing()
    {
        var product = NewPhone(stock: 1);

        Assert.Throws<DomainException>(() => product.RemoveStock(Quantity.Create(2)));

        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void PlacingOrder_RaisesOrderPlacedWithLines()
    {
        var phone = ProductId.New();
        var order = Order.Create(new CustomerId(Guid.NewGuid()), "TRY", DateTime.UtcNow);
        order.AddItem(phone, "Telefon X", Try(10_000m), Quantity.Create(2));

        Assert.Empty(order.DomainEvents); // adding items to a draft is not a business event yet

        order.Place(DateTime.UtcNow);

        var e = Assert.IsType<OrderPlacedDomainEvent>(Assert.Single(order.DomainEvents));
        Assert.Equal(order.Id, e.OrderId);
        Assert.Equal(new OrderPlacedLine(phone, 2), Assert.Single(e.Lines));
    }

    [Fact]
    public void EnsureStockFor_ThrowsWithBusinessMessage()
    {
        var product = NewPhone(stock: 1);

        var ex = Assert.Throws<DomainException>(() => product.EnsureStockFor(Quantity.Create(3)));

        Assert.Contains("Requested: 3, available: 1", ex.Message);
    }
}
