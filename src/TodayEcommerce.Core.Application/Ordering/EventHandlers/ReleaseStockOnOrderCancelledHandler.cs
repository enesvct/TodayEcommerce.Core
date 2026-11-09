using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering.Events;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Ordering.EventHandlers;

/// <summary>
/// The mirror image of <see cref="ReserveStockOnOrderPlacedHandler"/>: puts reserved stock back
/// when a confirmed order is cancelled. Runs from the outbox, so the cancel request itself only
/// touches the Order and never fails because someone else is buying the same product right now.
/// </summary>
/// <remarks>
/// Exactly-once for the stock: the AddStock changes and the outbox "processed" mark commit in the same
/// transaction. On a concurrency conflict everything rolls back and the outbox retries.
/// </remarks>
internal sealed class ReleaseStockOnOrderCancelledHandler : IDomainEventHandler<OrderCancelledDomainEvent>
{
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReleaseStockOnOrderCancelledHandler> _logger;

    public ReleaseStockOnOrderCancelledHandler(
        IProductRepository products,
        IUnitOfWork unitOfWork,
        ILogger<ReleaseStockOnOrderCancelledHandler> logger)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(OrderCancelledDomainEvent e, CancellationToken cancellationToken)
    {
        // "?? []": events stored before this field existed deserialize with null — they reserved nothing to release.
        var lines = e.StockToRelease ?? [];
        if (lines.Count == 0)
        {
            return; // cancelled before any stock was reserved
        }

        var products = (await _products.GetByIdsAsync(lines.Select(l => l.ProductId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.ProductId, out var product))
            {
                _logger.LogWarning("Order {OrderId}: product {ProductId} no longer exists; {Quantity} unit(s) not returned to stock",
                    e.OrderId, line.ProductId, line.Quantity);
                continue;
            }

            product.AddStock(Quantity.Create(line.Quantity));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Order {OrderId} cancelled; stock released for {Lines} line(s)", e.OrderId, lines.Count);
    }
}
