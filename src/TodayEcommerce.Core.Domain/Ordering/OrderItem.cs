using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.Ordering;

/// <summary>
/// A line of an order. Lives inside the Order aggregate: it can only be created and changed by
/// <see cref="Order"/> (constructor and mutators are internal), so the order's invariants hold.
/// </summary>
public sealed class OrderItem : Entity<OrderItemId>
{
    /// <summary>Reference to the Product aggregate by id only — never the Product object itself.</summary>
    public ProductId ProductId { get; }

    /// <summary>Snapshot of the product name at the time it was ordered.</summary>
    public string ProductName { get; }

    /// <summary>Snapshot of the unit price at the time it was ordered; later price changes don't affect it.</summary>
    public Money UnitPrice { get; }

    public Quantity Quantity { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>For EF Core materialization only.</summary>
    private OrderItem()
    {
        ProductName = null!;
        UnitPrice = null!;
        Quantity = null!;
    }

    internal OrderItem(ProductId productId, string productName, Money unitPrice, Quantity quantity)
        : base(OrderItemId.New())
    {
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    internal void IncreaseQuantity(Quantity additional)
    {
        Quantity = Quantity.Add(additional);
    }
}
