using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.Ordering.Events;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Ordering.EventHandlers;

/// <summary>
/// Reserves stock for a placed order — all lines or none — then confirms the order,
/// or cancels it when any line can't be fulfilled.
/// </summary>
/// <remarks>
/// Idempotency, two layers:
/// 1. State machine: only a Placed order is processed; a redelivered event finds it Confirmed/Cancelled and stops.
/// 2. Same transaction: the stock changes, the order status and the outbox "processed" mark commit together,
///    so "stock decreased but message not marked" cannot happen.
/// Concurrency: if another order changed the same product meanwhile, saving throws ConcurrencyException,
/// everything rolls back and the outbox retries with fresh stock levels.
/// </remarks>
internal sealed class ReserveStockOnOrderPlacedHandler : IDomainEventHandler<OrderPlacedDomainEvent>
{
    private readonly IOrderRepository _orders;
    private readonly IProductRepository _products;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReserveStockOnOrderPlacedHandler> _logger;

    public ReserveStockOnOrderPlacedHandler(
        IOrderRepository orders,
        IProductRepository products,
        IUnitOfWork unitOfWork,
        ILogger<ReserveStockOnOrderPlacedHandler> logger)
    {
        _orders = orders;
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(OrderPlacedDomainEvent e, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(e.OrderId, cancellationToken);
        if (order is null || order.Status != OrderStatus.Placed)
        {
            // Already handled (duplicate delivery) or no longer relevant → do nothing. This is the idempotency guard.
            _logger.LogInformation("Order {OrderId} is {Status}; skipping stock reservation", e.OrderId, order?.Status);
            return;
        }

        var products = (await _products.GetByIdsAsync(e.Lines.Select(l => l.ProductId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        // Phase 1 — check every line WITHOUT changing anything.
        var problem = FindFirstProblem(e.Lines, products);
        if (problem is not null)
        {
            order.Cancel(problem);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Order {OrderId} cancelled: {Reason}", e.OrderId, problem);
            return;
        }

        // Phase 2 — all lines are fulfillable: apply every change.
        foreach (var line in e.Lines)
        {
            products[line.ProductId].RemoveStock(Quantity.Create(line.Quantity));
        }

        order.Confirm();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Order {OrderId} confirmed; stock reserved for {Lines} line(s)", e.OrderId, e.Lines.Count);
    }

    private static string? FindFirstProblem(IReadOnlyList<OrderPlacedLine> lines, IReadOnlyDictionary<ProductId, Product> products)
    {
        // Sum per product, in case the same product appears on several lines.
        foreach (var group in lines.GroupBy(l => l.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product))
            {
                return $"Product {group.Key} no longer exists.";
            }

            var requested = Quantity.Create(group.Sum(l => l.Quantity));
            if (!product.HasStockFor(requested))
            {
                return $"Insufficient stock for '{product.Name}'. Requested: {requested.Value}, available: {product.StockQuantity}";
            }
        }

        return null;
    }
}
