using TodayEcommerce.Core.Application.Common.Events;

namespace TodayEcommerce.Core.Application.Catalog.Contracts;

// Catalog's published language: answers to Ordering's requests. Primitives only.

/// <summary>Every product of the order is now held for it.</summary>
public sealed record StockReservedIntegrationEvent(Guid OrderId) : IIntegrationEvent;

/// <summary>Nothing was reserved (all or nothing): at least one product was missing or short.</summary>
public sealed record StockReservationFailedIntegrationEvent(Guid OrderId, string Reason) : IIntegrationEvent;
