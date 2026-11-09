using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Repositories;

internal sealed class StockReservationRepository : IStockReservationRepository
{
    private readonly AppDbContext _dbContext;

    public StockReservationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<StockReservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        _dbContext.StockReservations.SingleOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

    public void Add(StockReservation reservation) => _dbContext.StockReservations.Add(reservation);
}
