using MediatR;

namespace TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;

/// <summary>
/// Places a new order. Returns the id of the created order.
/// </summary>
/// <remarks>
/// CustomerId comes from the request for now; once authentication exists it must be taken
/// from the logged-in user instead, otherwise anyone could order on behalf of anyone.
/// Prices are intentionally NOT part of the request: the server always uses the catalog price.
/// </remarks>
public sealed record CreateOrderCommand(
    Guid CustomerId,
    IReadOnlyList<CreateOrderItem> Items) : IRequest<Guid>;

public sealed record CreateOrderItem(Guid ProductId, int Quantity);
