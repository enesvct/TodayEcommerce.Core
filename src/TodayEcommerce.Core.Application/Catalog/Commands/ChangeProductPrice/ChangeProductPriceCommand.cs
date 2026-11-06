using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Application.Common.Caching;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Catalog.Commands.ChangeProductPrice;

/// <summary>Changes a product's price (in its existing currency).</summary>
public sealed record ChangeProductPriceCommand(Guid ProductId, decimal NewPrice) : IRequest;

internal sealed class ChangeProductPriceCommandValidator : AbstractValidator<ChangeProductPriceCommand>
{
    public ChangeProductPriceCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.NewPrice).GreaterThanOrEqualTo(0);
    }
}

internal sealed class ChangeProductPriceCommandHandler : IRequestHandler<ChangeProductPriceCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IProductSearchService _search;

    public ChangeProductPriceCommandHandler(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        IProductSearchService search)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _search = search;
    }

    public async Task Handle(ChangeProductPriceCommand request, CancellationToken cancellationToken)
    {
        // Commands read the source of truth (database), never the cache.
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        product.ChangePrice(Money.Create(request.NewPrice, product.Price.Currency));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidate AFTER the commit: deleting first would let a concurrent read re-cache the old price
        // before our change is saved. Delete (not update) so the next read loads fresh data.
        await _cache.RemoveAsync(CacheKeys.Product(request.ProductId), cancellationToken);

        // Keep the search index in sync (a price filter on stale data would show wrong results).
        await _search.IndexAsync(ProductSearchDocument.From(product), cancellationToken);
    }
}
