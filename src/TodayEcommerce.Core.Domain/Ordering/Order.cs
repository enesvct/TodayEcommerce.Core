using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.Ordering;

/// <summary>
/// Customer order. Aggregate root that owns its <see cref="OrderItem"/>s and controls the
/// Draft → Placed / Cancelled lifecycle.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = new();

    public CustomerId CustomerId { get; }
    public string Currency { get; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? PlacedAtUtc { get; private set; }

    /// <summary>Read-only view: callers can inspect lines but must use Order's methods to change them.</summary>
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public Money Total => _items.Aggregate(Money.Zero(Currency), (sum, item) => sum.Add(item.LineTotal));

    /// <summary>For EF Core materialization only.</summary>
    private Order()
    {
        Currency = null!;
    }

    private Order(OrderId id, CustomerId customerId, string currency, DateTime createdAtUtc)
        : base(id)
    {
        CustomerId = customerId;
        Currency = currency;
        Status = OrderStatus.Draft;
        CreatedAtUtc = createdAtUtc;
    }

    public static Order Create(CustomerId customerId, string currency, DateTime utcNow)
    {
        // Reuse Money's currency validation/normalization so both always agree.
        var normalizedCurrency = Money.Zero(currency).Currency;
        return new Order(OrderId.New(), customerId, normalizedCurrency, utcNow);
    }

    public void AddItem(ProductId productId, string productName, Money unitPrice, Quantity quantity)
    {
        EnsureStatus(OrderStatus.Draft, "add items");

        if (unitPrice.Currency != Currency)
        {
            throw new DomainException($"Item currency {unitPrice.Currency} does not match order currency {Currency}.");
        }

        var existing = _items.SingleOrDefault(i => i.ProductId == productId);
        if (existing is not null)
        {
            // Same product twice → one line with summed quantity (keeps the first price snapshot).
            existing.IncreaseQuantity(quantity);
            return;
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new DomainException("Product name is required for an order item.");
        }

        _items.Add(new OrderItem(productId, productName.Trim(), unitPrice, quantity));
    }

    public void RemoveItem(ProductId productId)
    {
        EnsureStatus(OrderStatus.Draft, "remove items");

        var item = _items.SingleOrDefault(i => i.ProductId == productId)
            ?? throw new DomainException($"Product {productId} is not in this order.");

        _items.Remove(item);
    }

    public void Place(DateTime utcNow)
    {
        EnsureStatus(OrderStatus.Draft, "place the order");

        if (_items.Count == 0)
        {
            throw new DomainException("An order must contain at least one item before it can be placed.");
        }

        Status = OrderStatus.Placed;
        PlacedAtUtc = utcNow;
        // Step 5: raise OrderCreatedDomainEvent here (→ Outbox → stock decrease).
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException("Order is already cancelled.");
        }

        Status = OrderStatus.Cancelled;
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Cannot {action} when order is {Status}.");
        }
    }
}
