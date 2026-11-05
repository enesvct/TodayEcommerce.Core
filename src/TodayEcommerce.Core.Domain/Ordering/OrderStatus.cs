namespace TodayEcommerce.Core.Domain.Ordering;

public enum OrderStatus
{
    /// <summary>Items can still be added or removed.</summary>
    Draft = 0,

    /// <summary>Order was submitted; lines are frozen.</summary>
    Placed = 1,

    /// <summary>Terminal state; no further changes allowed.</summary>
    Cancelled = 2,
}
