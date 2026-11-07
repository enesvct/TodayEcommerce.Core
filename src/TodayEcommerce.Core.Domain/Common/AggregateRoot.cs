namespace TodayEcommerce.Core.Domain.Common;

/// <summary>
/// The single entry point of an aggregate. Outside code may only hold references to, load and save
/// aggregate roots; everything inside the aggregate is changed through the root's methods,
/// so the root can enforce the aggregate's invariants.
/// </summary>
/// <remarks>
/// The root also records domain events for what happened. They are written to the outbox in the same
/// transaction as the aggregate's changes, then handled asynchronously.
/// </remarks>
public abstract class AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    protected AggregateRoot(TId id)
        : base(id)
    {
    }

    /// <summary>For ORM materialization only.</summary>
    protected AggregateRoot()
    {
    }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Only the aggregate itself can raise events — they describe its own state changes.</summary>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
