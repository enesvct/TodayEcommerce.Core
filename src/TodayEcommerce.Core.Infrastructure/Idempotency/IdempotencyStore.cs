using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.Idempotency;

internal sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public IdempotencyStore(AppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IdempotencyRecord?> FindAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        // AsNoTracking: always read what is committed, also right after our own save failed on a duplicate.
        return await _db.IdempotencyRecords.AsNoTracking()
            .Where(r => r.UserId == userId && r.Key == key)
            .Select(r => new IdempotencyRecord(r.RequestHash, r.ResultId))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(Guid userId, string key, IdempotencyRecord record) =>
        _db.IdempotencyRecords.Add(new IdempotencyRecordEntity
        {
            UserId = userId,
            Key = key,
            RequestHash = record.RequestHash,
            ResultId = record.ResultId,
            CreatedOnUtc = _clock.UtcNow,
        });
}
