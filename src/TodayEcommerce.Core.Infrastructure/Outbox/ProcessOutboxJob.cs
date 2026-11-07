using Hangfire;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>
/// The Hangfire job: every few seconds, deliver pending outbox messages.
/// </summary>
public sealed class ProcessOutboxJob
{
    public const string RecurringJobId = "process-outbox";

    /// <summary>Six-field cron with seconds: every 5 seconds.</summary>
    public const string Schedule = "*/5 * * * * *";

    private readonly OutboxProcessor _processor;

    public ProcessOutboxJob(OutboxProcessor processor)
    {
        _processor = processor;
    }

    // No Hangfire retries: the outbox already tracks attempts per message; a failing run just waits for the next one.
    // No overlapping runs on the same server (SKIP LOCKED would make overlap safe anyway, just wasteful).
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _processor.ProcessAsync(batchSize: 50, cancellationToken);
}
