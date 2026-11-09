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
/// <param name="IdempotencyKey">
/// Optional, chosen by the client (e.g. a GUID per checkout). Sending the same request again with the
/// same key returns the original order instead of placing a second one — safe retries after a timeout.
/// </param>
public sealed record CreateOrderCommand(
    IReadOnlyList<CreateOrderItem> Items,
    string? IdempotencyKey = null) : IRequest<Guid>;

public sealed record CreateOrderItem(Guid ProductId, int Quantity);
