using MediatR;

namespace TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;

/// <summary>
/// Read-only request: returns the product, or null when it does not exist.
/// Changes nothing in the system (CQRS query side).
/// </summary>
public sealed record GetProductByIdQuery(Guid ProductId) : IRequest<ProductDto?>;

/// <summary>
/// What the outside world sees. A flat, serializable shape — never the Product aggregate itself,
/// so the domain model can evolve without breaking API clients.
/// </summary>
public sealed record ProductDto(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool InStock);
