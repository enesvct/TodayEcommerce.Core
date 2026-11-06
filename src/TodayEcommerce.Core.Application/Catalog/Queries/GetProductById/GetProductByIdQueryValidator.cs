using FluentValidation;

namespace TodayEcommerce.Core.Application.Catalog.Queries.GetProductById;

internal sealed class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
    }
}
