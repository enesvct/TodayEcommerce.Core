namespace TodayEcommerce.Core.Infrastructure.Idempotency;

/// <summary>Row of idempotency_records. Primary key (user_id, key) is what makes duplicates impossible.</summary>
internal sealed class IdempotencyRecordEntity
{
    public Guid UserId { get; init; }
    public required string Key { get; init; }
    public required string RequestHash { get; init; }
    public Guid ResultId { get; init; }
    public DateTime CreatedOnUtc { get; init; }
}
