namespace TodayEcommerce.Core.Domain.Common;

/// <summary>
/// Something meaningful that already happened in the domain (always named in the past tense).
/// Our own marker instead of MediatR's INotification: the Domain may not depend on MediatR.
/// </summary>
/// <remarks>
/// Events are serialized to the outbox, so they carry only ids and primitive values
/// (no aggregates, no value objects without a public constructor).
/// </remarks>
public interface IDomainEvent
{
}

/// <summary>Non-generic view of an aggregate's pending events, so infrastructure can collect them.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
