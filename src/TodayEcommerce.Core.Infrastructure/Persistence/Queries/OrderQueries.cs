using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Application.Common;
using TodayEcommerce.Core.Application.Ordering.Queries.GetMyOrders;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Queries;

internal sealed class OrderQueries : IOrderQueries
{
    private readonly AppDbContext _db;

    public OrderQueries(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<OrderSummaryDto>> GetForCustomerAsync(
        Guid customerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Served by the (customer_id, created_at_utc DESC) index: find the customer, already sorted.
        var mine = _db.Orders.AsNoTracking().Where(o => o.CustomerId == new CustomerId(customerId));

        var total = await mine.LongCountAsync(cancellationToken);

        // Projection: only the columns the list needs — no change tracking, no full aggregates.
        // Lines are reduced to (price, quantity) so the total can be summed below.
        var rows = await mine
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id) // tie-breaker: a stable order, so no row shows up on two pages
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.Currency,
                o.CreatedAtUtc,
                Lines = o.Items.Select(i => new { i.UnitPrice.Amount, i.Quantity }).ToList(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new OrderSummaryDto(
                r.Id.Value,
                r.Status.ToString(),
                r.Lines.Sum(l => l.Amount * l.Quantity.Value),
                r.Currency,
                r.Lines.Count,
                r.CreatedAtUtc))
            .ToList();

        return new PagedResult<OrderSummaryDto>(items, total, page, pageSize);
    }
}
