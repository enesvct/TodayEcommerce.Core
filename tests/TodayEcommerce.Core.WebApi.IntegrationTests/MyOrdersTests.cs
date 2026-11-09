using System.Net;
using System.Net.Http.Json;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class MyOrdersTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly Guid _ayse = Guid.NewGuid();
    private readonly Guid _mehmet = Guid.NewGuid();

    public MyOrdersTests(PostgresFixture db)
    {
        _api = new ApiFactory(db.ConnectionString, "Testing");
    }

    public void Dispose() => _api.Dispose();

    private sealed record IdResponse(Guid Id);
    private sealed record Summary(Guid Id, string Status, decimal Total, string Currency, int ItemCount, DateTime CreatedAtUtc);
    private sealed record OrderPage(List<Summary> Items, long TotalCount, int Page, int PageSize);

    private HttpClient As(Guid user) => _api.CreateClient().As(TestTokens.For(user));

    private async Task<Guid> CreateProductAsync(decimal price)
    {
        var response = await _api.CreateClient().As(TestTokens.For(Guid.NewGuid(), "admin"))
            .PostAsJsonAsync("/api/products", new { name = "Liste Urunu", price, currency = "TRY", initialStock = 100 });
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private async Task<Guid> OrderAsync(Guid user, params (Guid ProductId, int Quantity)[] lines)
    {
        var response = await As(user).PostAsJsonAsync("/api/orders",
            new { items = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    [Fact]
    public async Task ShowsOnlyMyOrders_NewestFirst_WithTotals()
    {
        var phone = await CreateProductAsync(1_000m);
        var cover = await CreateProductAsync(50m);
        var first = await OrderAsync(_ayse, (phone, 1));
        var second = await OrderAsync(_ayse, (phone, 2), (cover, 1));
        await OrderAsync(_mehmet, (phone, 1));

        var page = await As(_ayse).GetFromJsonAsync<OrderPage>("/api/orders");

        Assert.Equal(2, page!.TotalCount); // Mehmet's order is not in Ayşe's list
        Assert.Equal(new[] { second, first }, page.Items.Select(o => o.Id));
        Assert.Equal(2_050m, page.Items[0].Total);
        Assert.Equal(2, page.Items[0].ItemCount);
    }

    [Fact]
    public async Task Paging_SplitsTheList_WithoutGapsOrDuplicates()
    {
        var phone = await CreateProductAsync(10m);
        var placed = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            placed.Add(await OrderAsync(_ayse, (phone, 1)));
        }

        var page1 = await As(_ayse).GetFromJsonAsync<OrderPage>("/api/orders?page=1&pageSize=2");
        var page2 = await As(_ayse).GetFromJsonAsync<OrderPage>("/api/orders?page=2&pageSize=2");
        var page3 = await As(_ayse).GetFromJsonAsync<OrderPage>("/api/orders?page=3&pageSize=2");

        Assert.Equal(5, page1!.TotalCount);
        var seen = page1.Items.Concat(page2!.Items).Concat(page3!.Items).Select(o => o.Id).ToList();
        Assert.Equal(Enumerable.Reverse(placed), seen);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task InvalidPaging_Is400(string query)
    {
        var response = await As(_ayse).GetAsync($"/api/orders?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoToken_Is401()
    {
        var response = await _api.CreateClient().GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
