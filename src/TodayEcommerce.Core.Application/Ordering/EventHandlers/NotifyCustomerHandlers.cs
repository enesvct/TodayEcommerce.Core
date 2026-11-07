using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Domain.Ordering.Events;

namespace TodayEcommerce.Core.Application.Ordering.EventHandlers;

/// <summary>
/// Stand-in for a notification service (e-mail/SMS/push). It only logs, but shows the point of events:
/// adding this reaction required no change to Order, to the stock handler, or to any command.
/// </summary>
/// <remarks>
/// A real sender is NOT naturally idempotent (a redelivered event would send the e-mail twice):
/// it would record "notification sent for event X" and skip duplicates.
/// </remarks>
internal sealed class NotifyCustomerHandlers :
    IDomainEventHandler<OrderConfirmedDomainEvent>,
    IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly ILogger<NotifyCustomerHandlers> _logger;

    public NotifyCustomerHandlers(ILogger<NotifyCustomerHandlers> logger)
    {
        _logger = logger;
    }

    public Task HandleAsync(OrderConfirmedDomainEvent e, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📧 NOTIFY customer {CustomerId}: order {OrderId} confirmed", e.CustomerId, e.OrderId);
        return Task.CompletedTask;
    }

    public Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📧 NOTIFY customer {CustomerId}: order {OrderId} cancelled — {Reason}", e.CustomerId, e.OrderId, e.Reason);
        return Task.CompletedTask;
    }
}
