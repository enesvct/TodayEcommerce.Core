using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

/// <summary>
/// Stages the integration event as an outbox row in the request's (or outbox handler's) DbContext.
/// Nothing is sent here: the row is committed by SaveChanges together with the change that caused it.
/// </summary>
internal sealed class OutboxIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly AppDbContext _db;
    private readonly IDateTimeProvider _clock;

    public OutboxIntegrationEventPublisher(AppDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public void Publish(IIntegrationEvent integrationEvent) =>
        _db.OutboxMessages.Add(OutboxSerializer.ToMessage(integrationEvent, _clock.UtcNow));
}
