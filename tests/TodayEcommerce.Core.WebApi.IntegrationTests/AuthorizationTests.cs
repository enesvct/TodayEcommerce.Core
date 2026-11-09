using System.Net;
using System.Net.Http.Json;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>Who may call what — checked over real HTTP, through the real middleware pipeline.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationTests : IDisposable
{
    private static readonly Guid Ayse = Guid.NewGuid();
    private static readonly Guid Mehmet = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly ApiFactory _api;

    public AuthorizationTests(PostgresFixture db)
    {
        _api = new ApiFactory(db.ConnectionString, "Testing");
    }

    public void Dispose() => _api.Dispose();

    private HttpClient Anonymous() => _api.CreateClient();
    private HttpClient As(Guid user, string? role = null) => _api.CreateClient().As(TestTokens.For(user, role));

    private async Task<Guid> CreateProductAsync(int stock = 10)
    {
        var response = await As(Admin, "admin").PostAsJsonAsync("/api/products",
            new { name = "Test Telefon", price = 1_000m, currency = "TRY", initialStock = stock });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private static object OrderOf(Guid productId) => new { items = new[] { new { productId, quantity = 1 } } };

    private sealed record IdResponse(Guid Id);
    private sealed record OrderResponse(Guid Id, Guid CustomerId);

    // ---------- 401: who are you? ----------

    [Fact]
    public async Task NoToken_Order_Is401()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/orders", OrderOf(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType); // ProblemDetails, not an empty body
    }

    [Fact]
    public async Task ExpiredToken_Is401()
    {
        var expired = TestTokens.For(Ayse, expiresAtUtc: DateTime.UtcNow.AddMinutes(-5));

        var response = await _api.CreateClient().As(expired).PostAsJsonAsync("/api/orders", OrderOf(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Is401_EvenIfItClaimsAdmin()
    {
        // Someone mints their own "admin" token. Without our key the signature cannot match.
        var forged = TestTokens.For(Mehmet, "admin", signingKey: "attacker-key-attacker-key-attacker-key-1234");

        var response = await _api.CreateClient().As(forged).PostAsJsonAsync("/api/products",
            new { name = "Sahte", price = 1m, currency = "TRY", initialStock = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- 403: are you allowed? ----------

    [Fact]
    public async Task Customer_CreatingAProduct_Is403()
    {
        var response = await As(Ayse).PostAsJsonAsync("/api/products",
            new { name = "Yetkisiz", price = 1m, currency = "TRY", initialStock = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Customer_ChangingAPrice_Is403()
    {
        var productId = await CreateProductAsync();

        var response = await As(Ayse).PutAsJsonAsync($"/api/products/{productId}/price", new { price = 1m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- open on purpose ----------

    [Fact]
    public async Task Anyone_CanBrowseAProduct_WithoutAnAccount()
    {
        var productId = await CreateProductAsync();

        var response = await Anonymous().GetAsync($"/api/products/{productId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- ownership ----------

    [Fact]
    public async Task Order_BelongsToTheTokensUser_ACustomerIdInTheBodyIsIgnored()
    {
        var productId = await CreateProductAsync();

        // Mehmet tries to place the order in Ayşe's name.
        var create = await As(Mehmet).PostAsJsonAsync("/api/orders",
            new { customerId = Ayse, items = new[] { new { productId, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var orderId = (await create.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var order = await As(Mehmet).GetFromJsonAsync<OrderResponse>($"/api/orders/{orderId}");
        Assert.Equal(Mehmet, order!.CustomerId);
    }

    [Fact]
    public async Task SomeoneElsesOrder_Is404_IndistinguishableFromAMissingOne()
    {
        var productId = await CreateProductAsync();
        var create = await As(Ayse).PostAsJsonAsync("/api/orders", OrderOf(productId));
        var ayseOrderId = (await create.Content.ReadFromJsonAsync<IdResponse>())!.Id;

        var own = await As(Ayse).GetAsync($"/api/orders/{ayseOrderId}");
        var someoneElses = await As(Mehmet).GetAsync($"/api/orders/{ayseOrderId}");
        var missing = await As(Mehmet).GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
