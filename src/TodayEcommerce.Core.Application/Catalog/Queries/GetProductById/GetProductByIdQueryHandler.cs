using MediatR;
using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;

internal sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IProductRepository _productRepository;

    public GetProductByIdQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken);

        if (product is null)
        {
            return null;
        }

        return new ProductDto(
            product.Id.Value,
            product.Name,
            product.Price.Amount,
            product.Price.Currency,
            product.StockQuantity,
            InStock: product.StockQuantity > 0);
    }
}
