namespace TodayEcommerce.Core.Domain.Ordering;

public enum OrderStatus
{
    /// <summary>Items can still be added or removed.</summary>
    Draft = 0,

    /// <summary>Order was submitted; lines are frozen; stock not yet reserved.</summary>
    Placed = 1,

    /// <summary>Terminal state; no further changes allowed.</summary>
    Cancelled = 2,

    /// <summary>Stock was reserved for every line (set asynchronously after OrderPlaced).</summary>
    Confirmed = 3,
}
