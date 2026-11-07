using Hangfire;
using Microsoft.Extensions.Hosting;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>Registers (or updates) the recurring outbox job when the application starts.</summary>
internal sealed class OutboxJobScheduler : IHostedService
{
    private readonly IRecurringJobManager _recurringJobs;

    public OutboxJobScheduler(IRecurringJobManager recurringJobs)
    {
        _recurringJobs = recurringJobs;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _recurringJobs.AddOrUpdate<ProcessOutboxJob>(
            ProcessOutboxJob.RecurringJobId,
            job => job.ExecuteAsync(CancellationToken.None), // Hangfire substitutes its own shutdown token
            ProcessOutboxJob.Schedule);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
