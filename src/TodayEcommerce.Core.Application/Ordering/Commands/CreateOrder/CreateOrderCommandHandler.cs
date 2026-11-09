using MediatR;
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

    public CreateOrderCommandHandler(
        IProductRepository productRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _currentUser = currentUser;
    }

    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
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
        var order = Order.Create(new CustomerId(_currentUser.UserId), currency, now);

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

        // 3. Persist: repository tracks the new order, unit of work commits in one transaction.
        _orderRepository.Add(order);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Id.Value;
    }
}
