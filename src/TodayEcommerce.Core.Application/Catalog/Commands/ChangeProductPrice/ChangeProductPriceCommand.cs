using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
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

    public ChangeProductPriceCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ChangeProductPriceCommand request, CancellationToken cancellationToken)
    {
        // Commands read the source of truth (database), never the cache.
        var product = await _productRepository.GetByIdAsync(new ProductId(request.ProductId), cancellationToken)
            ?? throw new NotFoundException("Product", request.ProductId);

        // Raises ProductPriceChangedDomainEvent. Saving writes it to the outbox in the same transaction;
        // cache invalidation and search re-indexing then happen in the event handlers — after the commit, with retries.
        product.ChangePrice(Money.Create(request.NewPrice, product.Price.Currency));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
