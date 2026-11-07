using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering.Events;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.Ordering;

/// <summary>
/// Customer order. Aggregate root that owns its <see cref="OrderItem"/>s and controls the lifecycle:
/// Draft → Placed → Confirmed (stock reserved), or → Cancelled (from Draft or Placed).
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = new();

    public CustomerId CustomerId { get; }
    public string Currency { get; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; }
    public DateTime? PlacedAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

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

        // → Outbox (same transaction as the order) → stock decrease, handled asynchronously.
        Raise(new OrderPlacedDomainEvent(
            Id,
            CustomerId,
            _items.Select(i => new OrderPlacedLine(i.ProductId, i.Quantity.Value)).ToList()));
    }

    /// <summary>Stock was reserved for every line: the order will be fulfilled.</summary>
    public void Confirm()
    {
        EnsureStatus(OrderStatus.Placed, "confirm the order");

        Status = OrderStatus.Confirmed;
        Raise(new OrderConfirmedDomainEvent(Id, CustomerId));
    }

    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainException("Order is already cancelled.");
        }

        if (Status == OrderStatus.Confirmed)
        {
            // Stock is already reserved; undoing that is a separate business process (returns/refunds).
            throw new DomainException("A confirmed order cannot be cancelled directly.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A cancellation reason is required.");
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason.Trim();
        Raise(new OrderCancelledDomainEvent(Id, CustomerId, CancellationReason));
    }

    private void EnsureStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new DomainException($"Cannot {action} when order is {Status}.");
        }
    }
}
