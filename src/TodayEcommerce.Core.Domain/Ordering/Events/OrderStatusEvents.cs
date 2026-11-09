using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Ordering.Events;

public sealed record OrderConfirmedDomainEvent(OrderId OrderId, CustomerId CustomerId) : IDomainEvent;

/// <param name="StockToRelease">
/// Stock that had been reserved for this order and must go back to the shelf. Empty when the order was
/// cancelled before any stock was reserved (Draft/Placed) — returning stock then would create phantom stock.
/// </param>
public sealed record OrderCancelledDomainEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    string Reason,
    IReadOnlyList<ReservedStockLine> StockToRelease) : IDomainEvent;

public sealed record ReservedStockLine(ProductId ProductId, int Quantity);
