using TodayEcommerce.Core.Application.Common.Events;

namespace TodayEcommerce.Core.Application.Ordering.Contracts;

// Ordering's published language. Other modules may depend on this namespace — and only on this one.
// Primitives only, so a change inside Ordering's domain can never break a consumer.

/// <summary>An order was placed and needs these products.</summary>
public sealed record OrderPlacedIntegrationEvent(Guid OrderId, IReadOnlyList<OrderedProduct> Products) : IIntegrationEvent;

public sealed record OrderedProduct(Guid ProductId, int Quantity);

/// <summary>An order was cancelled (by the customer or because stock could not be reserved).</summary>
public sealed record OrderCancelledIntegrationEvent(Guid OrderId) : IIntegrationEvent;

/// <summary>
/// Compensation: stock was reserved for an order that had already been cancelled meanwhile
/// (the messages crossed). Ordering asks for the reservation to be undone.
/// </summary>
public sealed record ReservationNoLongerNeededIntegrationEvent(Guid OrderId) : IIntegrationEvent;
