using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Application.Ordering.Commands.CancelOrder;

/// <summary>The signed-in customer cancels one of their own orders.</summary>
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : IRequest;

internal sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

internal sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _orders;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;

    public CancelOrderCommandHandler(IOrderRepository orders, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    {
        _orders = orders;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(new OrderId(request.OrderId), cancellationToken);

        // Someone else's order is "not found", exactly like in GetOrderById: no hint that the id exists.
        if (order is null || order.CustomerId.Value != _currentUser.UserId)
        {
            throw new NotFoundException("Order", request.OrderId);
        }

        // Rules (already cancelled? reason given?) live in the aggregate → DomainException → 422.
        order.Cancel(request.Reason);

        // Only the Order is saved here. Returning reserved stock happens via the outbox
        // (OrderCancelled → ReleaseStockOnOrderCancelledHandler). If the stock reservation is saving this
        // very order at the same moment, the row-version check turns one of the two into a 409.
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
