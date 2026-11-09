using MediatR;

namespace TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;

/// <summary>
/// Places a new order. Returns the id of the created order.
/// </summary>
/// <remarks>
/// Deliberately missing from the request: the customer (taken from the signed-in user, see
/// <see cref="Abstractions.ICurrentUser"/>) and prices (always taken from the catalog).
/// Whatever the caller could fake is not something the caller gets to send.
/// </remarks>
public sealed record CreateOrderCommand(IReadOnlyList<CreateOrderItem> Items) : IRequest<Guid>;

public sealed record CreateOrderItem(Guid ProductId, int Quantity);
