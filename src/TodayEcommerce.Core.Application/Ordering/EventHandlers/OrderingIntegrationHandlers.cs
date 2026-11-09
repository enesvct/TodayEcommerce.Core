using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Contracts;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Application.Ordering.Contracts;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.Ordering.Events;

namespace TodayEcommerce.Core.Application.Ordering.EventHandlers;

/// <summary>
/// Translates Ordering's internal domain events into its published contracts. The domain stays free to
/// change; only this class has to keep the promise made to other modules.
/// </summary>
internal sealed class PublishOrderingIntegrationEvents :
    IDomainEventHandler<OrderPlacedDomainEvent>,
    IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;

    public PublishOrderingIntegrationEvents(IIntegrationEventPublisher publisher, IUnitOfWork unitOfWork)
    {
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderPlacedDomainEvent e, CancellationToken cancellationToken)
    {
        _publisher.Publish(new OrderPlacedIntegrationEvent(
            e.OrderId.Value,
            e.Lines.Select(l => new OrderedProduct(l.ProductId.Value, l.Quantity)).ToList()));
        await _unitOfWork.SaveChangesAsync(cancellationToken); // commits with this message's "processed" mark
    }

    public async Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        _publisher.Publish(new OrderCancelledIntegrationEvent(e.OrderId.Value));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Ordering's side of the stock saga: reacts to the Catalog's answer. Only Catalog's Contracts are used here —
/// Ordering never touches a Product.
/// </summary>
internal sealed class ApplyStockReservationResult :
    IIntegrationEventHandler<StockReservedIntegrationEvent>,
    IIntegrationEventHandler<StockReservationFailedIntegrationEvent>
{
    private readonly IOrderRepository _orders;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ApplyStockReservationResult> _logger;

    public ApplyStockReservationResult(
        IOrderRepository orders,
        IIntegrationEventPublisher publisher,
        IUnitOfWork unitOfWork,
        ILogger<ApplyStockReservationResult> logger)
    {
        _orders = orders;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(StockReservedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(new OrderId(e.OrderId), cancellationToken);

        switch (order?.Status)
        {
            case OrderStatus.Placed:
                order.Confirm();
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Order {OrderId} confirmed: stock reserved", e.OrderId);
                break;

            case OrderStatus.Cancelled:
                // The messages crossed: the customer cancelled while the Catalog was reserving. There is no
                // rollback across modules — only a compensating step: ask the Catalog to undo the reservation.
                _publisher.Publish(new ReservationNoLongerNeededIntegrationEvent(e.OrderId));
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Order {OrderId} was already cancelled when its stock got reserved; compensating", e.OrderId);
                break;

            default:
                // Confirmed already (redelivery) or unknown: nothing to do.
                _logger.LogInformation("Order {OrderId} is {Status}; ignoring StockReserved", e.OrderId, order?.Status);
                break;
        }
    }

    public async Task HandleAsync(StockReservationFailedIntegrationEvent e, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(new OrderId(e.OrderId), cancellationToken);
        if (order?.Status != OrderStatus.Placed)
        {
            return; // already cancelled by the customer, or a redelivery
        }

        order.Cancel(e.Reason);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled: {Reason}", e.OrderId, e.Reason);
    }
}
