namespace TodayEcommerce.Core.Application.Common.Events;

/// <summary>
/// A message one module publishes for OTHER modules: its "published language".
/// </summary>
/// <remarks>
/// Domain event vs integration event:
/// - A domain event is internal to its module (rich types like OrderId, free to change any time).
/// - An integration event is a promise to other modules: primitives only (Guid, int, string), lives in the
///   module's Contracts namespace, and changes like a public API — additively, or as a new version.
/// Other modules may depend on a module's Contracts, never on its domain or handlers.
/// </remarks>
public interface IIntegrationEvent
{
}

/// <summary>
/// Reacts to another module's integration event. Same rules as domain event handlers: runs from the outbox,
/// at-least-once, must be idempotent; throwing makes the outbox retry.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes an integration event through the outbox. <see cref="Publish"/> only stages it: it is stored by
/// the next IUnitOfWork.SaveChangesAsync, in the same transaction as the change that caused it — so a
/// rolled-back change can never have announced itself to other modules.
/// </summary>
public interface IIntegrationEventPublisher
{
    void Publish(IIntegrationEvent integrationEvent);
}
