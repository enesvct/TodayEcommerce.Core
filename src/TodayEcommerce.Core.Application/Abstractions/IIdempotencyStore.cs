namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Remembers which (user, Idempotency-Key) pairs were already handled and what they produced,
/// so a retried request gets the original result instead of doing the work twice.
/// </summary>
/// <remarks>
/// <see cref="Add"/> only stages the record: it is saved by <see cref="IUnitOfWork.SaveChangesAsync"/>
/// in the SAME transaction as the work itself. Either both the order and its key are stored, or neither.
/// </remarks>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid userId, string key, CancellationToken cancellationToken = default);

    void Add(Guid userId, string key, IdempotencyRecord record);
}

/// <param name="RequestHash">Fingerprint of the request, to detect one key reused for a different request.</param>
/// <param name="ResultId">What the first request produced (e.g. the order id).</param>
public sealed record IdempotencyRecord(string RequestHash, Guid ResultId);
