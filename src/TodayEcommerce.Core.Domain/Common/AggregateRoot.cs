namespace TodayEcommerce.Core.Domain.Common;

/// <summary>
/// The single entry point of an aggregate. Outside code may only hold references to, load and save
/// aggregate roots; everything inside the aggregate is changed through the root's methods,
/// so the root can enforce the aggregate's invariants.
/// </summary>
/// <remarks>
/// Domain events will be collected here in a later step (Outbox pattern).
/// </remarks>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    protected AggregateRoot(TId id)
        : base(id)
    {
    }
}
