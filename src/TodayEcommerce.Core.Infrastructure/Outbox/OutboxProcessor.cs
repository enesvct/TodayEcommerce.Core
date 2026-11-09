using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>
/// Delivers pending outbox messages to their domain event handlers.
/// Scheduling (how often this runs) is a separate concern: see <see cref="ProcessOutboxJob"/>.
/// </summary>
/// <remarks>
/// Guarantees and their limits:
/// - At-least-once: a message is marked processed only after its handlers succeed. A crash in between
///   means it is handled again → handlers must be idempotent.
/// - Safe with several workers: each message is locked with FOR UPDATE SKIP LOCKED, so two workers
///   never handle the same message at the same time.
/// - Handlers that write to the database share the message's transaction: their changes and the
///   "processed" mark commit together (effectively exactly-once for database side effects).
/// - Exponential back-off: after each failure the message waits longer before the next try
///   (5s, 15s, 30s ... 1h), so short outages recover quickly and long ones aren't hammered.
/// - Poison messages: after <see cref="MaxAttempts"/> failures (~2 hours of retrying) a message is no longer
///   picked up and needs a human (LastError says why; resetting attempt_count re-enables it).
/// </remarks>
public sealed class OutboxProcessor
{
    /// <summary>Wait before attempt n+1, indexed by failures so far − 1. Sum ≈ 1h 55m.</summary>
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
    };

    public const int MaxAttempts = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, IDateTimeProvider clock, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public static TimeSpan DelayAfter(int failedAttempts) => Backoff[Math.Min(failedAttempts, Backoff.Length) - 1];

    /// <summary>Processes up to <paramref name="batchSize"/> pending messages. Returns how many succeeded.</summary>
    public async Task<int> ProcessAsync(int batchSize = 20, CancellationToken cancellationToken = default)
    {
        var candidateIds = await LoadCandidateIdsAsync(batchSize, cancellationToken);

        var succeeded = 0;
        foreach (var id in candidateIds)
        {
            if (await ProcessOneAsync(id, cancellationToken))
            {
                succeeded++;
            }
        }

        return succeeded;
    }

    /// <summary>
    /// Keeps processing while there is work, up to <paramref name="maxRounds"/> batches. One event often causes
    /// the next (OrderPlaced → StockReserved → OrderConfirmed): draining delivers the whole chain in one job run
    /// instead of one hop per 5-second tick. Failed messages wait for their back-off, so they don't loop here.
    /// </summary>
    public async Task<int> DrainAsync(int batchSize = 20, int maxRounds = 10, CancellationToken cancellationToken = default)
    {
        var total = 0;
        for (var round = 0; round < maxRounds; round++)
        {
            var succeeded = await ProcessAsync(batchSize, cancellationToken);
            total += succeeded;
            if (succeeded == 0)
            {
                break;
            }
        }

        return total;
    }

    private async Task<List<Guid>> LoadCandidateIdsAsync(int batchSize, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Oldest first, so events for the same aggregate are handled in the order they happened.
        // Failed messages are skipped until their back-off delay has passed.
        var now = _clock.UtcNow;
        return await db.OutboxMessages.AsNoTracking()
            .Where(m => m.ProcessedOnUtc == null
                        && m.AttemptCount < MaxAttempts
                        && (m.NextAttemptOnUtc == null || m.NextAttemptOnUtc <= now))
            .OrderBy(m => m.OccurredOnUtc)
            .Select(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ProcessOneAsync(Guid messageId, CancellationToken cancellationToken)
    {
        // New scope per message: fresh DbContext (no leftovers from a previous failure) and fresh handlers.
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Lock the row; if another worker already holds it, skip it instead of waiting.
            var message = await db.OutboxMessages
                .FromSqlInterpolated($"""
                    SELECT * FROM outbox_messages
                    WHERE id = {messageId} AND processed_on_utc IS NULL
                    FOR UPDATE SKIP LOCKED
                    """)
                .SingleOrDefaultAsync(cancellationToken);

            if (message is null)
            {
                return false; // processed meanwhile, or another worker is on it
            }

            // Continue the trace of the request that raised the event: every log line below — and the
            // outbox rows of any events the handlers raise — carry the same TraceId as that request.
            using var activity = StartActivity(message);
            using var logScope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["OutboxMessageId"] = message.Id,
                ["EventType"] = ShortTypeName(message.Type),
                ["Attempt"] = message.AttemptCount + 1,
            });

            try
            {
                await dispatcher.DispatchAsync(OutboxSerializer.ToEvent(message), cancellationToken);

                message.ProcessedOnUtc = _clock.UtcNow;
                message.LastError = null;
                message.NextAttemptOnUtc = null;
                await db.SaveChangesAsync(cancellationToken); // handler DB changes + processed mark, one commit
                await transaction.CommitAsync(cancellationToken);
                return true;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                _logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed; will retry", message.Id, message.Type);
                await RecordFailureAsync(messageId, ex, cancellationToken);
                return false;
            }
        }
    }

    private static Activity StartActivity(OutboxMessage message)
    {
        var activity = new Activity("outbox.process");
        if (message.TraceParent is not null)
        {
            activity.SetParentId(message.TraceParent);
        }

        return activity.Start();
    }

    /// <summary>"Namespace.OrderPlacedDomainEvent, Assembly" → "OrderPlacedDomainEvent".</summary>
    private static string ShortTypeName(string type)
    {
        var fullName = type.Split(',')[0];
        return fullName[(fullName.LastIndexOf('.') + 1)..];
    }

    /// <summary>Separate scope/transaction: the failed one was rolled back, but the attempt must still be counted.</summary>
    private async Task RecordFailureAsync(Guid messageId, Exception ex, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var error = $"{ex.GetType().Name}: {ex.Message}";
        if (error.Length > 2000)
        {
            error = error[..2000];
        }

        var attempts = await db.OutboxMessages
            .Where(m => m.Id == messageId)
            .Select(m => m.AttemptCount)
            .SingleAsync(cancellationToken) + 1;

        var nextAttempt = _clock.UtcNow + DelayAfter(attempts);

        await db.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AttemptCount, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.NextAttemptOnUtc, nextAttempt), cancellationToken);

        if (attempts >= MaxAttempts)
        {
            _logger.LogError("Outbox message {MessageId} gave up after {Attempts} attempts: {Error}", messageId, attempts, error);
        }
        else
        {
            _logger.LogInformation("Outbox message {MessageId}: attempt {Attempts} failed, next try at {NextAttempt:HH:mm:ss}", messageId, attempts, nextAttempt);
        }
    }
}
