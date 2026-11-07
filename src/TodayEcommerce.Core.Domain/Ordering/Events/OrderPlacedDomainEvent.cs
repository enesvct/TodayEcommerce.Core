using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Ordering.Events;

/// <summary>
/// An order was placed. Carries the lines so consumers (e.g. stock reservation) don't need to
/// load the order — and see exactly what was ordered at that moment.
/// </summary>
public sealed record OrderPlacedDomainEvent(
    OrderId OrderId,
    CustomerId CustomerId,
    IReadOnlyList<OrderPlacedLine> Lines) : IDomainEvent;

public sealed record OrderPlacedLine(ProductId ProductId, int Quantity);
