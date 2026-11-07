using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>
/// Delivers pending outbox messages to their domain event handlers.
/// Scheduling (how often this runs) is a separate concern — Step 5b wires it to Hangfire.
/// </summary>
/// <remarks>
/// Guarantees and their limits:
/// - At-least-once: a message is marked processed only after its handlers succeed. A crash in between
///   means it is handled again → handlers must be idempotent.
/// - Safe with several workers: each message is locked with FOR UPDATE SKIP LOCKED, so two workers
///   never handle the same message at the same time.
/// - Handlers that write to the database share the message's transaction: their changes and the
///   "processed" mark commit together (effectively exactly-once for database side effects).
/// - Poison messages: after <see cref="MaxAttempts"/> failures a message is no longer picked up and
///   needs a human (LastError says why).
/// </remarks>
public sealed class OutboxProcessor
{
    public const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

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

    private async Task<List<Guid>> LoadCandidateIdsAsync(int batchSize, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Oldest first, so events for the same aggregate are handled in the order they happened.
        // A failed message simply waits for the next run (natural back-off).
        return await db.OutboxMessages.AsNoTracking()
            .Where(m => m.ProcessedOnUtc == null && m.AttemptCount < MaxAttempts)
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
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

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

            try
            {
                var domainEvent = OutboxSerializer.ToDomainEvent(message);
                await dispatcher.DispatchAsync(domainEvent, cancellationToken);

                message.ProcessedOnUtc = DateTime.UtcNow;
                message.LastError = null;
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

        await db.OutboxMessages
            .Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.AttemptCount, attempts)
                .SetProperty(m => m.LastError, error), cancellationToken);

        if (attempts >= MaxAttempts)
        {
            _logger.LogError("Outbox message {MessageId} gave up after {Attempts} attempts: {Error}", messageId, attempts, error);
        }
    }
}
