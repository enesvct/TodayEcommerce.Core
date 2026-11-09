using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Contracts;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Application.Ordering.Contracts;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Catalog.EventHandlers;

/// <summary>
/// The Catalog owns stock. Ordering never changes it: it publishes what it needs (OrderPlaced) and the
/// Catalog answers (StockReserved / StockReservationFailed). Only Ordering's Contracts are used here.
/// </summary>
/// <remarks>
/// Idempotent: a reservation is recorded per order, so a redelivered OrderPlaced finds it and stops.
/// Consistent: stock changes, the reservation record and the answer (an outbox row) commit together.
/// On a concurrency conflict (another order took the same product) all of it rolls back and is retried.
/// </remarks>
internal sealed class ReserveStockOnOrderPlaced : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    private readonly IProductRepository _products;
    private readonly IStockReservationRepository _reservations;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReserveStockOnOrderPlaced> _logger;

    public ReserveStockOnOrderPlaced(
        IProductRepository products,
        IStockReservationRepository reservations,
        IIntegrationEventPublisher publisher,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ILogger<ReserveStockOnOrderPlaced> logger)
    {
        _products = products;
        _reservations = reservations;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (await _reservations.GetByOrderIdAsync(e.OrderId, cancellationToken) is not null)
        {
            _logger.LogInformation("Stock for order {OrderId} was already reserved; skipping", e.OrderId);
            return;
        }

        // Sum per product, in case the same product appears on several lines.
        var wanted = e.Products
            .GroupBy(p => new ProductId(p.ProductId))
            .Select(g => new ReservedItem(g.Key, g.Sum(p => p.Quantity)))
            .ToList();

        var products = (await _products.GetByIdsAsync(wanted.Select(w => w.ProductId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        // Phase 1 — check every line WITHOUT changing anything (all or nothing).
        var problem = FindFirstProblem(wanted, products);
        if (problem is not null)
        {
            _publisher.Publish(new StockReservationFailedIntegrationEvent(e.OrderId, problem));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Stock reservation for order {OrderId} failed: {Reason}", e.OrderId, problem);
            return;
        }

        // Phase 2 — take every unit off the shelf and remember for whom.
        foreach (var item in wanted)
        {
            products[item.ProductId].RemoveStock(Quantity.Create(item.Quantity));
        }

        _reservations.Add(StockReservation.Create(e.OrderId, wanted, _clock.UtcNow));
        _publisher.Publish(new StockReservedIntegrationEvent(e.OrderId));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Stock reserved for order {OrderId}: {Lines} product(s)", e.OrderId, wanted.Count);
    }

    private static string? FindFirstProblem(IEnumerable<ReservedItem> wanted, IReadOnlyDictionary<ProductId, Product> products)
    {
        foreach (var item in wanted)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                return $"Product {item.ProductId} no longer exists.";
            }

            if (!product.HasStockFor(Quantity.Create(item.Quantity)))
            {
                return $"Insufficient stock for '{product.Name}'. Requested: {item.Quantity}, available: {product.StockQuantity}";
            }
        }

        return null;
    }
}

/// <summary>
/// Puts reserved stock back — when the order is cancelled, or when Ordering reports (compensation) that a
/// reservation arrived for an order that was already cancelled. "Is there anything to give back?" is
/// answered from the Catalog's own reservation record, never from the message.
/// </summary>
internal sealed class ReleaseStockReservation :
    IIntegrationEventHandler<OrderCancelledIntegrationEvent>,
    IIntegrationEventHandler<ReservationNoLongerNeededIntegrationEvent>
{
    private readonly IProductRepository _products;
    private readonly IStockReservationRepository _reservations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReleaseStockReservation> _logger;

    public ReleaseStockReservation(
        IProductRepository products,
        IStockReservationRepository reservations,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ILogger<ReleaseStockReservation> logger)
    {
        _products = products;
        _reservations = reservations;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public Task HandleAsync(OrderCancelledIntegrationEvent e, CancellationToken cancellationToken) =>
        ReleaseAsync(e.OrderId, "order cancelled", cancellationToken);

    public Task HandleAsync(ReservationNoLongerNeededIntegrationEvent e, CancellationToken cancellationToken) =>
        ReleaseAsync(e.OrderId, "compensation: order was cancelled before the reservation arrived", cancellationToken);

    private async Task ReleaseAsync(Guid orderId, string why, CancellationToken cancellationToken)
    {
        var reservation = await _reservations.GetByOrderIdAsync(orderId, cancellationToken);

        // No reservation (cancelled before anything was reserved, or reservation failed) or already released:
        // nothing to give back. This is what makes phantom stock impossible.
        var items = reservation?.Release(_clock.UtcNow) ?? [];
        if (items.Count == 0)
        {
            return;
        }

        var products = (await _products.GetByIdsAsync(items.Select(i => i.ProductId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var item in items)
        {
            if (products.TryGetValue(item.ProductId, out var product))
            {
                product.AddStock(Quantity.Create(item.Quantity));
            }
            else
            {
                _logger.LogWarning("Order {OrderId}: product {ProductId} no longer exists; {Quantity} unit(s) not returned",
                    orderId, item.ProductId, item.Quantity);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Stock released for order {OrderId} ({Why})", orderId, why);
    }
}
