using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.UnitTests.Catalog;

public class StockReservationTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ReservedItem Phone = new(ProductId.New(), 2);

    [Fact]
    public void Create_IsActive_AndHoldsTheItems()
    {
        var reservation = StockReservation.Create(Guid.NewGuid(), [Phone], Now);

        Assert.True(reservation.IsActive);
        Assert.Equal(Phone, Assert.Single(reservation.Items));
    }

    [Fact]
    public void Create_WithoutItems_Throws()
    {
        Assert.Throws<DomainException>(() => StockReservation.Create(Guid.NewGuid(), [], Now));
    }

    [Fact]
    public void Release_ReturnsTheItemsOnce_ASecondReleaseReturnsNothing()
    {
        var reservation = StockReservation.Create(Guid.NewGuid(), [Phone], Now);

        var first = reservation.Release(Now.AddMinutes(1));
        var second = reservation.Release(Now.AddMinutes(2)); // a redelivered "cancelled" message

        Assert.Equal(Phone, Assert.Single(first));
        Assert.Empty(second);
        Assert.Equal(StockReservationStatus.Released, reservation.Status);
        Assert.Equal(Now.AddMinutes(1), reservation.ReleasedAtUtc);
    }
}
