namespace TodayEcommerce.Core.Domain.Ordering;

/// <summary>
/// Persistence contract for the Order aggregate. Implemented in Infrastructure.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default);

    void Add(Order order);
}
