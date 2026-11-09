using FluentValidation;

namespace TodayEcommerce.Core.Application.Ordering.Commands.CreateOrder;

/// <summary>
/// Shape checks only (cheap, no database). Business rules such as stock availability
/// stay in the domain model, which remains the last line of defence.
/// </summary>
internal sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public const int MaxItemsPerOrder = 50;
    public const int MaxQuantityPerItem = 100;

    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("An order must contain at least one item.")
            .Must(items => items.Count <= MaxItemsPerOrder)
                .WithMessage($"An order cannot contain more than {MaxItemsPerOrder} items.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty();
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, MaxQuantityPerItem);
        });
    }
}
