using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Application.Common.Events;

/// <summary>
/// Reacts to a domain event. Runs asynchronously from the outbox, possibly MORE THAN ONCE for the same
/// event (at-least-once delivery), so every handler must be idempotent: handling twice = handling once.
/// Throwing makes the outbox retry the event later.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
