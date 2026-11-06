using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.UnitTests.SharedKernel;

public class MoneyTests
{
    [Fact]
    public void Create_WithNegativeAmount_Throws()
    {
        // Arrange
        const decimal negativeAmount = -1m;

        // Act
        var act = () => Money.Create(negativeAmount, "TRY");

        // Assert
        Assert.Throws<DomainException>(act);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TL")]
    [InlineData("EURO")]
    public void Create_WithInvalidCurrency_Throws(string currency)
    {
        Assert.Throws<DomainException>(() => Money.Create(10m, currency));
    }

    [Fact]
    public void Create_NormalizesCurrencyAndRoundsToTwoDecimals()
    {
        var money = Money.Create(10.555m, " try ");

        Assert.Equal(10.56m, money.Amount);
        Assert.Equal("TRY", money.Currency);
    }

    [Fact]
    public void Equality_IsBasedOnValues_NotOnReference()
    {
        var first = Money.Create(100m, "TRY");
        var second = Money.Create(100m, "try");

        Assert.Equal(first, second);
        Assert.False(ReferenceEquals(first, second));
    }

    [Fact]
    public void Add_WithSameCurrency_ReturnsNewInstanceAndLeavesOriginalUnchanged()
    {
        var original = Money.Create(100m, "TRY");

        var result = original.Add(Money.Create(50m, "TRY"));

        Assert.Equal(Money.Create(150m, "TRY"), result);
        Assert.Equal(100m, original.Amount); // immutability: the original never changes
    }

    [Fact]
    public void Add_WithDifferentCurrency_Throws()
    {
        var lira = Money.Create(100m, "TRY");
        var dollar = Money.Create(100m, "USD");

        Assert.Throws<DomainException>(() => lira.Add(dollar));
    }

    [Fact]
    public void Multiply_ByQuantity_ReturnsUnitPriceTimesQuantity()
    {
        var unitPrice = Money.Create(12.50m, "TRY");

        var total = unitPrice.Multiply(Quantity.Create(3));

        Assert.Equal(Money.Create(37.50m, "TRY"), total);
    }
}
