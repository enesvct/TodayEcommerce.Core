using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Application.Ordering.Queries.GetOrderById;

/// <summary>
/// Returns the signed-in customer's order with its lines, or null when it does not exist
/// OR belongs to someone else — the two cases are indistinguishable on purpose.
/// </summary>
public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDto?>;

public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    string Currency,
    DateTime CreatedAtUtc,
    DateTime? PlacedAtUtc,
    string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

internal sealed class GetOrderByIdQueryValidator : AbstractValidator<GetOrderByIdQuery>
{
    public GetOrderByIdQueryValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

internal sealed class GetOrderByIdQueryHandler : IRequestHandler<GetOrderByIdQuery, OrderDto?>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetOrderByIdQueryHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<OrderDto?> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);
        // Someone else's order → "not found", not "forbidden": a 403 would confirm that the id exists,
        // letting an attacker probe which order ids are real.
        if (order is null || order.CustomerId.Value != _currentUser.UserId)
        {
            return null;
        }

        return new OrderDto(
            order.Id.Value,
            order.CustomerId.Value,
            order.Status.ToString(),
            order.Total.Amount,
            order.Currency,
            order.CreatedAtUtc,
            order.PlacedAtUtc,
            order.CancellationReason,
            order.Items
                .Select(i => new OrderItemDto(
                    i.ProductId.Value,
                    i.ProductName,
                    i.UnitPrice.Amount,
                    i.Quantity.Value,
                    i.LineTotal.Amount))
                .ToList());
    }
}
