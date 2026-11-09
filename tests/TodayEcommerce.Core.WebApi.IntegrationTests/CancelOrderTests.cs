using System.Net;
using System.Net.Http.Json;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CancelOrderTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly Guid _ayse = Guid.NewGuid();

    public CancelOrderTests(PostgresFixture db)
    {
        _api = new ApiFactory(db.ConnectionString, "Testing");
    }

    public void Dispose() => _api.Dispose();

    private sealed record IdResponse(Guid Id);
    private sealed record OrderView(Guid Id, string Status, string? CancellationReason);

    private HttpClient As(Guid user) => _api.CreateClient().As(TestTokens.For(user));

    private async Task<Guid> AyseOrdersAsync()
    {
        var product = await _api.CreateClient().As(TestTokens.For(Guid.NewGuid(), "admin"))
            .PostAsJsonAsync("/api/products", new { name = "Iptal Urunu", price = 10m, currency = "TRY", initialStock = 5 });
        var productId = (await product.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var order = await As(_ayse).PostAsJsonAsync("/api/orders", new { items = new[] { new { productId, quantity = 1 } } });
        return (await order.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    [Fact]
    public async Task Owner_Cancels_204_AndTheOrderShowsTheReason()
    {
        var orderId = await AyseOrdersAsync();

        var response = await As(_ayse).PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "Vazgectim" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var order = await As(_ayse).GetFromJsonAsync<OrderView>($"/api/orders/{orderId}");
        Assert.Equal(("Cancelled", "Vazgectim"), (order!.Status, order.CancellationReason));
    }

    [Fact]
    public async Task SomeoneElse_Cancelling_Is404_AndNothingChanges()
    {
        var orderId = await AyseOrdersAsync();

        var response = await As(Guid.NewGuid()).PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "ha ha" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var order = await As(_ayse).GetFromJsonAsync<OrderView>($"/api/orders/{orderId}");
        Assert.Equal("Placed", order!.Status);
    }

    [Fact]
    public async Task NoReason_Is400()
    {
        var orderId = await AyseOrdersAsync();

        var response = await As(_ayse).PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CancellingTwice_Is422()
    {
        var orderId = await AyseOrdersAsync();
        await As(_ayse).PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "bir" });

        var response = await As(_ayse).PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason = "iki" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
