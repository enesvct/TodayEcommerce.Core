using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Catalog.Events;

/// <summary>Common shape of every product event: read models (cache, search) only need to know which product changed.</summary>
public interface IProductChangedEvent : IDomainEvent
{
    ProductId ProductId { get; }
}

public sealed record ProductCreatedDomainEvent(ProductId ProductId) : IProductChangedEvent;

public sealed record ProductRenamedDomainEvent(ProductId ProductId, string OldName, string NewName) : IProductChangedEvent;

public sealed record ProductPriceChangedDomainEvent(
    ProductId ProductId,
    decimal OldAmount,
    decimal NewAmount,
    string Currency) : IProductChangedEvent;

public sealed record ProductStockChangedDomainEvent(ProductId ProductId, int NewStockQuantity) : IProductChangedEvent;
