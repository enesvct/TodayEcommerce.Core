using MediatR;
using Microsoft.AspNetCore.Mvc;
using TodayEcommerce.Core.Application.Catalog.Commands.ChangeProductPrice;
using TodayEcommerce.Core.Application.Catalog.Commands.CreateProduct;
using TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;

namespace TodayEcommerce.Core.WebApi.Controllers;

/// <summary>
/// Thin controller: translates HTTP to a MediatR request and the result back to HTTP. No business logic.
/// </summary>
[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly ISender _sender;

    public ProductsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var id = await _sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await _sender.Send(new GetProductByIdQuery(id), cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPut("{id:guid}/price")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePrice(Guid id, ChangePriceRequest request, CancellationToken cancellationToken)
    {
        await _sender.Send(new ChangeProductPriceCommand(id, request.Price), cancellationToken);
        return NoContent();
    }

    /// <summary>HTTP body shape; the id comes from the route, so it isn't part of the body.</summary>
    public sealed record ChangePriceRequest(decimal Price);
}
