using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.UnitTests.Catalog;

public class ProductTests
{
    private static Product CreatePhone(int stock = 10) =>
        Product.Create("Phone", Money.Create(10_000m, "TRY"), stock);

    [Fact]
    public void Create_WithValidData_SetsPropertiesAndGeneratesId()
    {
        var product = Product.Create("  Phone  ", Money.Create(10_000m, "TRY"), 5);

        Assert.Equal("Phone", product.Name);
        Assert.Equal(5, product.StockQuantity);
        Assert.NotEqual(Guid.Empty, product.Id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Throws(string name)
    {
        Assert.Throws<DomainException>(() => Product.Create(name, Money.Create(1m, "TRY"), 0));
    }

    [Fact]
    public void Create_WithTooLongName_Throws()
    {
        var longName = new string('x', Product.NameMaxLength + 1);

        Assert.Throws<DomainException>(() => Product.Create(longName, Money.Create(1m, "TRY"), 0));
    }

    [Fact]
    public void Create_WithNegativeStock_Throws()
    {
        Assert.Throws<DomainException>(() => Product.Create("Phone", Money.Create(1m, "TRY"), -1));
    }

    [Fact]
    public void Create_WithZeroStock_IsAllowed()
    {
        var product = CreatePhone(stock: 0);

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_WithEnoughStock_DecreasesStock()
    {
        var product = CreatePhone(stock: 10);

        product.RemoveStock(Quantity.Create(3));

        Assert.Equal(7, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_ExactlyAllStock_LeavesZero()
    {
        var product = CreatePhone(stock: 2);

        product.RemoveStock(Quantity.Create(2));

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void RemoveStock_WithInsufficientStock_ThrowsAndKeepsStockUnchanged()
    {
        var product = CreatePhone(stock: 1);

        Assert.Throws<DomainException>(() => product.RemoveStock(Quantity.Create(2)));
        Assert.Equal(1, product.StockQuantity);
    }

    [Fact]
    public void AddStock_IncreasesStock()
    {
        var product = CreatePhone(stock: 0);

        product.AddStock(Quantity.Create(4));

        Assert.Equal(4, product.StockQuantity);
    }

    [Fact]
    public void ChangePrice_WithSameCurrency_UpdatesPrice()
    {
        var product = CreatePhone();

        product.ChangePrice(Money.Create(12_000m, "TRY"));

        Assert.Equal(Money.Create(12_000m, "TRY"), product.Price);
    }

    [Fact]
    public void ChangePrice_WithDifferentCurrency_Throws()
    {
        var product = CreatePhone();

        Assert.Throws<DomainException>(() => product.ChangePrice(Money.Create(300m, "USD")));
    }

    [Fact]
    public void TwoProducts_WithSameData_AreNotEqual_BecauseIdentityDiffers()
    {
        var first = CreatePhone();
        var second = CreatePhone();

        Assert.NotEqual(first, second); // entities compare by Id, unlike value objects
    }
}
