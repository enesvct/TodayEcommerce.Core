using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Repositories;

internal sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _dbContext;

    public OrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Owned items are loaded automatically with the order — the aggregate always comes back whole.
    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default) =>
        _dbContext.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);

    public void Add(Order order) => _dbContext.Orders.Add(order);
}
