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

    /// <summary>
    /// W3C trace context of the request that raised the event (e.g. "00-&lt;trace-id&gt;-&lt;span-id&gt;-01").
    /// The envelope, not the letter: the domain event stays free of technical metadata. The processor
    /// continues this trace, so background work logs the same TraceId as the request that caused it.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>Null = not processed yet. This is the "queue".</summary>
    public DateTime? ProcessedOnUtc { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Null = try as soon as possible. After a failure: not before this time (exponential back-off).</summary>
    public DateTime? NextAttemptOnUtc { get; set; }

    public string? LastError { get; set; }
}
