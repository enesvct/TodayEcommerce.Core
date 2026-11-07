namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>
/// A domain event persisted in the same transaction as the aggregate change that raised it.
/// Infrastructure-only: the domain never sees this type.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>"Namespace.TypeName, AssemblyName" — enough to deserialize the event back.</summary>
    public required string Type { get; init; }

    /// <summary>The event as JSON (stored as PostgreSQL jsonb, so it is also queryable for debugging).</summary>
    public required string Content { get; init; }

    public DateTime OccurredOnUtc { get; init; }

    /// <summary>Null = not processed yet. This is the "queue".</summary>
    public DateTime? ProcessedOnUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}
