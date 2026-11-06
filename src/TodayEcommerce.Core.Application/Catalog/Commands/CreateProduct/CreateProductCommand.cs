using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Application.Catalog.Commands.CreateProduct;

/// <summary>Adds a product to the catalog. Returns the new product's id.</summary>
public sealed record CreateProductCommand(
    string Name,
    decimal Price,
    string Currency,
    int InitialStock) : IRequest<Guid>;

internal sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
    }
}

internal sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductSearchService _search;

    public CreateProductCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork, IProductSearchService search)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _search = search;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var product = Product.Create(request.Name, Money.Create(request.Price, request.Currency), request.InitialStock);

        _productRepository.Add(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit: the index must never contain a product the database doesn't have.
        // Known gap (fixed in Step 5 with the Outbox): if the process dies right here, the index misses this product.
        await _search.IndexAsync(ProductSearchDocument.From(product), cancellationToken);

        return product.Id.Value;
    }
}
