using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;

/// <summary>
/// Orchestrates the use-case; the business rules themselves live in the Order and Product aggregates.
/// </summary>
internal sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICurrentUser _currentUser;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    public CreateOrderCommandHandler(
        IProductRepository productRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser,
        IIdempotencyStore idempotencyStore,
        ILogger<CreateOrderCommandHandler> logger)
    {
        _productRepository = productRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _currentUser = currentUser;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var key = request.IdempotencyKey;
        var requestHash = key is null ? null : Fingerprint(request);

        // 0. A retry of a request we already handled? Answer with the original order — and do it BEFORE
        //    any business check: the first request may have bought the last unit, so re-checking stock
        //    would wrongly tell the client "out of stock" for an order that actually succeeded.
        if (key is not null)
        {
            var previous = await _idempotencyStore.FindAsync(userId, key, cancellationToken);
            if (previous is not null)
            {
                _logger.LogInformation("Idempotency key {IdempotencyKey} seen before (order {OrderId})", key, previous.ResultId);
                return Replay(previous, key, requestHash!);
            }
        }

        // 1. Load every referenced product in ONE query (no N+1).
        var productIds = request.Items.Select(i => new ProductId(i.ProductId)).Distinct().ToList();
        var products = (await _productRepository.GetByIdsAsync(productIds, cancellationToken))
            .ToDictionary(p => p.Id);

        var missing = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != default)
        {
            throw new NotFoundException("Product", missing);
        }

        // 2. Build the order. The order's currency follows the catalog; mixing currencies is rejected by Order.AddItem.
        var now = _dateTimeProvider.UtcNow;
        var currency = products[productIds[0]].Price.Currency;
        var order = Order.Create(new CustomerId(userId), currency, now);

        foreach (var item in request.Items)
        {
            var product = products[new ProductId(item.ProductId)];
            var quantity = Quantity.Create(item.Quantity);

            // Early, friendly check only. The authoritative stock decrease happens asynchronously:
            // OrderPlaced event → outbox → ReserveStockOnOrderPlacedHandler (with concurrency control).
            product.EnsureStockFor(quantity);

            // Name and price are copied from the catalog = snapshot at ordering time.
            order.AddItem(product.Id, product.Name, product.Price, quantity);
        }

        order.Place(now);

        // 3. Persist: the order, its outbox events and the idempotency record commit in ONE transaction.
        _orderRepository.Add(order);
        if (key is not null)
        {
            _idempotencyStore.Add(userId, key, new IdempotencyRecord(requestHash!, order.Id.Value));
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateIdempotencyKeyException) when (key is not null)
        {
            // A twin request (same user, same key) passed step 0 at the same moment and committed first.
            // The database's unique key stopped us from creating a second order; answer like the winner did.
            var winner = await _idempotencyStore.FindAsync(userId, key, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency record vanished after a duplicate-key conflict.");
            return Replay(winner, key, requestHash!);
        }

        _logger.LogInformation("Order {OrderId} placed by customer {CustomerId} with {Lines} line(s), total {Total} {Currency}",
            order.Id.Value, userId, order.Items.Count, order.Total.Amount, order.Currency);
        return order.Id.Value;
    }

    private static Guid Replay(IdempotencyRecord previous, string key, string requestHash) =>
        previous.RequestHash == requestHash
            ? previous.ResultId
            : throw new IdempotencyKeyReusedException(key);

    /// <summary>Same items in any order → same fingerprint.</summary>
    private static string Fingerprint(CreateOrderCommand request)
    {
        var canonical = string.Join(';', request.Items
            .OrderBy(i => i.ProductId)
            .ThenBy(i => i.Quantity)
            .Select(i => $"{i.ProductId:N}:{i.Quantity}"));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("CreateOrder|" + canonical)));
    }
}
