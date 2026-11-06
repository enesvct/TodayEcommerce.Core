using TodayEcommerce.Core.Application.Catalog.Commands.ChangeProductPrice;
using TodayEcommerce.Core.Application.Catalog.Commands.CreateProduct;
using TodayEcommerce.Core.Application.Catalog.Queries.SearchProducts;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.UnitTests.Fakes;

namespace TodayEcommerce.Core.Application.UnitTests.Catalog;

public class ProductSearchTests
{
    private readonly ApplicationTestHost _host = new();

    [Fact]
    public async Task CreatingProduct_IndexesIt_AfterSaving()
    {
        var id = await _host.Mediator.Send(new CreateProductCommand("Telefon X", 10_000m, "TRY", 3));

        var doc = _host.Search.Indexed[id];
        Assert.Equal("Telefon X", doc.Name);
        Assert.Equal("TRY", doc.Currency);
        Assert.True(doc.InStock);
        Assert.Equal(1, _host.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task ChangingPrice_ReindexesProduct()
    {
        var id = await _host.Mediator.Send(new CreateProductCommand("Telefon X", 10_000m, "TRY", 3));

        await _host.Mediator.Send(new ChangeProductPriceCommand(id, 12_000m));

        Assert.Equal(12_000m, _host.Search.Indexed[id].Price);
    }

    [Fact]
    public async Task InvalidProduct_IsNeitherSavedNorIndexed()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _host.Mediator.Send(new CreateProductCommand("", -1m, "TRY", 3)));

        Assert.Empty(_host.Search.Indexed);
    }

    [Fact]
    public async Task Search_TrimsTextAndPassesFilters()
    {
        await _host.Mediator.Send(new SearchProductsQuery("  telefn  ", 100m, 5_000m, InStockOnly: true, Page: 2, PageSize: 10));

        var criteria = _host.Search.LastCriteria!;
        Assert.Equal("telefn", criteria.Text);
        Assert.Equal(100m, criteria.MinPrice);
        Assert.Equal(5_000m, criteria.MaxPrice);
        Assert.True(criteria.InStockOnly);
        Assert.Equal(2, criteria.Page);
    }

    [Fact]
    public async Task Search_WithBlankText_MeansNoTextFilter()
    {
        await _host.Mediator.Send(new SearchProductsQuery("   ", null, null));

        Assert.Null(_host.Search.LastCriteria!.Text);
    }

    [Theory]
    [InlineData(500, 100, 1, 20)]  // min > max
    [InlineData(null, null, 0, 20)] // page 0
    [InlineData(null, null, 1, 51)] // page too large
    [InlineData(-1, null, 1, 20)]   // negative price
    public async Task Search_WithInvalidCriteria_IsRejected_BeforeReachingTheSearchEngine(
        int? min, int? max, int page, int pageSize)
    {
        var query = new SearchProductsQuery("telefon", min, max, false, page, pageSize);

        await Assert.ThrowsAsync<ValidationException>(() => _host.Mediator.Send(query));
        Assert.Null(_host.Search.LastCriteria);
    }
}
