using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Ordering.Events;

public sealed record OrderConfirmedDomainEvent(OrderId OrderId, CustomerId CustomerId) : IDomainEvent;

public sealed record OrderCancelledDomainEvent(OrderId OrderId, CustomerId CustomerId, string Reason) : IDomainEvent;
