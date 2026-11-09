using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.Catalog;

public readonly record struct StockReservationId(Guid Value)
{
    public static StockReservationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public enum StockReservationStatus
{
    Active = 0,
    Released = 1,
}

/// <summary>One product line held for an order.</summary>
public sealed record ReservedItem(ProductId ProductId, int Quantity);

/// <summary>
/// Catalog's own record of "this much stock is held for that order". It is what lets the Catalog answer,
/// without asking Ordering, "did I reserve anything for order X — and did I already give it back?"
/// </summary>
/// <remarks>
/// The order is referenced by a plain Guid: Catalog knows that orders exist (through Ordering's published
/// contracts), but not Ordering's types. One reservation per order (unique in the database).
/// </remarks>
public sealed class StockReservation : AggregateRoot<StockReservationId>
{
    private readonly List<ReservedItem> _items = new();

    public Guid OrderId { get; }
    public StockReservationStatus Status { get; private set; }
    public DateTime ReservedAtUtc { get; }
    public DateTime? ReleasedAtUtc { get; private set; }

    public IReadOnlyCollection<ReservedItem> Items => _items.AsReadOnly();

    public bool IsActive => Status == StockReservationStatus.Active;

    /// <summary>For EF Core materialization only.</summary>
    private StockReservation()
    {
    }

    private StockReservation(Guid orderId, IEnumerable<ReservedItem> items, DateTime utcNow)
        : base(StockReservationId.New())
    {
        OrderId = orderId;
        _items.AddRange(items);
        Status = StockReservationStatus.Active;
        ReservedAtUtc = utcNow;
    }

    /// <summary>
    /// Records the reservation. Taking the units off the shelf is done on the products themselves
    /// (<see cref="Product.RemoveStock"/>), in the same transaction.
    /// </summary>
    public static StockReservation Create(Guid orderId, IReadOnlyCollection<ReservedItem> items, DateTime utcNow)
    {
        if (orderId == Guid.Empty)
        {
            throw new DomainException("A reservation must belong to an order.");
        }

        if (items.Count == 0)
        {
            throw new DomainException("A reservation must hold at least one item.");
        }

        return new StockReservation(orderId, items, utcNow);
    }

    /// <summary>
    /// Marks the stock as given back and returns what must go back on the shelf.
    /// Releasing twice returns nothing the second time — so a redelivered message can't double the stock.
    /// </summary>
    public IReadOnlyCollection<ReservedItem> Release(DateTime utcNow)
    {
        if (!IsActive)
        {
            return Array.Empty<ReservedItem>();
        }

        Status = StockReservationStatus.Released;
        ReleasedAtUtc = utcNow;
        return Items;
    }
}

public interface IStockReservationRepository
{
    Task<StockReservation?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);

    void Add(StockReservation reservation);
}
