using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Ordering.Events;

public sealed record OrderConfirmedDomainEvent(OrderId OrderId, CustomerId CustomerId) : IDomainEvent;

/// <remarks>
/// Says nothing about stock on purpose: whether stock was held for this order is the Catalog's knowledge
/// (its StockReservation), not the Order's.
/// </remarks>
public sealed record OrderCancelledDomainEvent(OrderId OrderId, CustomerId CustomerId, string Reason) : IDomainEvent;
