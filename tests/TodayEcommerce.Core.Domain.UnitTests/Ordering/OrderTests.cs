using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.UnitTests.Ordering;

public class OrderTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CustomerId Customer = new(Guid.NewGuid());

    private static Order CreateDraftOrder() => Order.Create(Customer, "TRY", Now);

    private static Money Try(decimal amount) => Money.Create(amount, "TRY");

    [Fact]
    public void Create_StartsAsEmptyDraft()
    {
        var order = CreateDraftOrder();

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Empty(order.Items);
        Assert.Equal(Try(0m), order.Total);
        Assert.Equal(Now, order.CreatedAtUtc);
    }

    [Fact]
    public void AddItem_CalculatesTotalFromLines()
    {
        var order = CreateDraftOrder();

        order.AddItem(ProductId.New(), "Phone", Try(100m), Quantity.Create(2));
        order.AddItem(ProductId.New(), "Case", Try(25m), Quantity.Create(1));

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(Try(225m), order.Total);
    }

    [Fact]
    public void AddItem_SameProductTwice_MergesIntoOneLine()
    {
        var order = CreateDraftOrder();
        var phoneId = ProductId.New();

        order.AddItem(phoneId, "Phone", Try(100m), Quantity.Create(1));
        order.AddItem(phoneId, "Phone", Try(100m), Quantity.Create(2));

        var line = Assert.Single(order.Items);
        Assert.Equal(Quantity.Create(3), line.Quantity);
    }

    [Fact]
    public void AddItem_WithDifferentCurrency_Throws()
    {
        var order = CreateDraftOrder();

        Assert.Throws<DomainException>(() =>
            order.AddItem(ProductId.New(), "Phone", Money.Create(100m, "USD"), Quantity.Create(1)));
    }

    [Fact]
    public void PriceSnapshot_IsNotAffected_WhenProductPriceChangesLater()
    {
        var product = Product.Create("Phone", Try(10_000m), 5);
        var order = CreateDraftOrder();
        order.AddItem(product.Id, product.Name, product.Price, Quantity.Create(1));

        product.ChangePrice(Try(12_000m));

        Assert.Equal(Try(10_000m), Assert.Single(order.Items).UnitPrice);
    }

    [Fact]
    public void RemoveItem_RemovesLine()
    {
        var order = CreateDraftOrder();
        var phoneId = ProductId.New();
        order.AddItem(phoneId, "Phone", Try(100m), Quantity.Create(1));

        order.RemoveItem(phoneId);

        Assert.Empty(order.Items);
    }

    [Fact]
    public void RemoveItem_NotInOrder_Throws()
    {
        var order = CreateDraftOrder();

        Assert.Throws<DomainException>(() => order.RemoveItem(ProductId.New()));
    }

    [Fact]
    public void Place_WithoutItems_Throws()
    {
        var order = CreateDraftOrder();

        Assert.Throws<DomainException>(() => order.Place(Now));
    }

    [Fact]
    public void Place_WithItems_MovesToPlaced()
    {
        var order = CreateDraftOrder();
        order.AddItem(ProductId.New(), "Phone", Try(100m), Quantity.Create(1));
        var placedAt = Now.AddMinutes(5);

        order.Place(placedAt);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(placedAt, order.PlacedAtUtc);
    }

    [Fact]
    public void PlacedOrder_CannotBeModified()
    {
        var order = CreateDraftOrder();
        var phoneId = ProductId.New();
        order.AddItem(phoneId, "Phone", Try(100m), Quantity.Create(1));
        order.Place(Now);

        Assert.Throws<DomainException>(() => order.AddItem(ProductId.New(), "Case", Try(25m), Quantity.Create(1)));
        Assert.Throws<DomainException>(() => order.RemoveItem(phoneId));
        Assert.Throws<DomainException>(() => order.Place(Now));
    }

    [Fact]
    public void Cancel_FromDraftOrPlaced_MovesToCancelled()
    {
        var draft = CreateDraftOrder();
        var placed = CreateDraftOrder();
        placed.AddItem(ProductId.New(), "Phone", Try(100m), Quantity.Create(1));
        placed.Place(Now);

        draft.Cancel("customer request");
        placed.Cancel("customer request");

        Assert.Equal(OrderStatus.Cancelled, draft.Status);
        Assert.Equal(OrderStatus.Cancelled, placed.Status);
    }

    [Fact]
    public void Cancel_Twice_Throws()
    {
        var order = CreateDraftOrder();
        order.Cancel("customer request");

        Assert.Throws<DomainException>(() => order.Cancel("customer request"));
    }

    [Fact]
    public void CancelledOrder_CannotBeModified()
    {
        var order = CreateDraftOrder();
        order.Cancel("customer request");

        Assert.Throws<DomainException>(() => order.AddItem(ProductId.New(), "Phone", Try(100m), Quantity.Create(1)));
    }

    private static Order CreatePlacedOrder()
    {
        var order = CreateDraftOrder();
        order.AddItem(ProductId.New(), "Phone", Try(100m), Quantity.Create(1));
        order.Place(Now);
        return order;
    }

    [Fact]
    public void Confirm_FromPlaced_MovesToConfirmed()
    {
        var order = CreatePlacedOrder();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_IsOnlyAllowedOnce_SoARedeliveredEventCannotConfirmTwice()
    {
        var order = CreatePlacedOrder();
        order.Confirm();

        Assert.Throws<DomainException>(() => order.Confirm());
    }

    [Fact]
    public void Confirm_FromDraft_Throws()
    {
        Assert.Throws<DomainException>(() => CreateDraftOrder().Confirm());
    }

    [Fact]
    public void ConfirmedOrder_CannotBeCancelledDirectly()
    {
        var order = CreatePlacedOrder();
        order.Confirm();

        Assert.Throws<DomainException>(() => order.Cancel("changed my mind"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutReason_Throws(string reason)
    {
        Assert.Throws<DomainException>(() => CreatePlacedOrder().Cancel(reason));
    }

    [Fact]
    public void Cancel_StoresReason()
    {
        var order = CreatePlacedOrder();

        order.Cancel("  Insufficient stock  ");

        Assert.Equal("Insufficient stock", order.CancellationReason);
    }
}
