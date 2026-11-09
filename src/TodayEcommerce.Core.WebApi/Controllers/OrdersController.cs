using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;
using TodayEcommerce.Core.Application.Common;
using TodayEcommerce.Core.Application.Ordering.Commands.CancelOrder;
using TodayEcommerce.Core.Application.Ordering.Queries.GetMyOrders;
using TodayEcommerce.Core.Application.Ordering.Queries.GetOrderById;

namespace TodayEcommerce.Core.WebApi.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize] // already required by the fallback policy; stated here so the intent is visible
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public sealed class OrdersController : ControllerBase
{
    private readonly ISender _sender;

    public OrdersController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // A retry with the same key returns the SAME order id (and the same 201) — never a second order.
        var id = await _sender.Send(new CreateOrderCommand(request.Items, idempotencyKey), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    /// <summary>HTTP body shape. The key travels as a header, the customer comes from the token.</summary>
    public sealed record CreateOrderRequest(IReadOnlyList<CreateOrderItem> Items);

    /// <summary>The signed-in customer's orders, newest first. Example: /api/orders?page=2&amp;pageSize=10</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetMine([FromQuery] GetMyOrdersQuery query, CancellationToken cancellationToken)
    {
        return Ok(await _sender.Send(query, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _sender.Send(new GetOrderByIdQuery(id), cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Cancel one of your orders (also after it was confirmed); reserved stock is returned in the background.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new CancelOrderCommand(id, request.Reason), cancellationToken);
        return NoContent();
    }

    public sealed record CancelOrderRequest(string Reason);
}
