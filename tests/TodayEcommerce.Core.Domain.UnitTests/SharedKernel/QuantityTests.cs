using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.UnitTests.SharedKernel;

public class QuantityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Create_WithLessThanOne_Throws(int value)
    {
        Assert.Throws<DomainException>(() => Quantity.Create(value));
    }

    [Fact]
    public void Add_SumsValues()
    {
        var result = Quantity.Create(2).Add(Quantity.Create(3));

        Assert.Equal(Quantity.Create(5), result);
    }

    [Fact]
    public void Add_WhenResultOverflowsInt_ThrowsInsteadOfWrappingToNegative()
    {
        var huge = Quantity.Create(int.MaxValue);

        Assert.Throws<OverflowException>(() => huge.Add(Quantity.Create(1)));
    }
}
