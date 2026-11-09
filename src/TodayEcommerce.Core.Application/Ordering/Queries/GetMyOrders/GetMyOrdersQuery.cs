using FluentValidation;
using MediatR;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common;

namespace TodayEcommerce.Core.Application.Ordering.Queries.GetMyOrders;

/// <summary>The signed-in customer's orders, newest first, one page at a time.</summary>
public sealed record GetMyOrdersQuery(int Page = 1, int PageSize = 20) : IRequest<PagedResult<OrderSummaryDto>>;

/// <summary>A row in "my orders": enough for a list screen; details come from GET /api/orders/{id}.</summary>
public sealed record OrderSummaryDto(
    Guid Id,
    string Status,
    decimal Total,
    string Currency,
    int ItemCount,
    DateTime CreatedAtUtc);

/// <summary>
/// Read side of Ordering. Unlike the repository it does not load aggregates: it asks the database for
/// exactly the columns a screen needs (CQRS — reads don't have to go through the write model).
/// </summary>
public interface IOrderQueries
{
    Task<PagedResult<OrderSummaryDto>> GetForCustomerAsync(Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default);
}

internal sealed class GetMyOrdersQueryValidator : AbstractValidator<GetMyOrdersQuery>
{
    public const int MaxPageSize = 100;

    public GetMyOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize); // nobody downloads 1M orders in one call
    }
}

internal sealed class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IOrderQueries _orders;
    private readonly ICurrentUser _currentUser;

    public GetMyOrdersQueryHandler(IOrderQueries orders, ICurrentUser currentUser)
    {
        _orders = orders;
        _currentUser = currentUser;
    }

    public Task<PagedResult<OrderSummaryDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken) =>
        _orders.GetForCustomerAsync(_currentUser.UserId, request.Page, request.PageSize, cancellationToken);
}
