using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>"Buy" pressed twice, or a mobile app retrying after a timeout — over real HTTP and PostgreSQL.</summary>
[Collection(ApiCollection.Name)]
public sealed class IdempotencyTests : IDisposable
{
    private readonly ApiFactory _api;
    private readonly Guid _customer = Guid.NewGuid();

    public IdempotencyTests(PostgresFixture db)
    {
        _api = new ApiFactory(db.ConnectionString, "Testing");
    }

    public void Dispose() => _api.Dispose();

    private sealed record IdResponse(Guid Id);

    private async Task<Guid> CreateProductAsync()
    {
        var admin = _api.CreateClient().As(TestTokens.For(Guid.NewGuid(), "admin"));
        var response = await admin.PostAsJsonAsync("/api/products",
            new { name = "Idempotent Telefon", price = 1_000m, currency = "TRY", initialStock = 50 });
        return (await response.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private HttpRequestMessage OrderRequest(Guid productId, string? key, int quantity = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { items = new[] { new { productId, quantity } } }),
        };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return request;
    }

    private HttpClient Customer() => _api.CreateClient().As(TestTokens.For(_customer));

    private async Task<int> OrdersInDatabaseAsync()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Orders.CountAsync(o => o.CustomerId == new CustomerId(_customer));
    }

    [Fact]
    public async Task SameKeyTwice_SameOrderAndSame201_OneOrderInTheDatabase()
    {
        var productId = await CreateProductAsync();
        var key = Guid.NewGuid().ToString();

        var first = await Customer().SendAsync(OrderRequest(productId, key));
        var retry = await Customer().SendAsync(OrderRequest(productId, key));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode); // same answer, not an error
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal(1, await OrdersInDatabaseAsync());
    }

    [Fact]
    public async Task TenIdenticalRequestsAtTheSameMoment_ExactlyOneOrder()
    {
        var productId = await CreateProductAsync();
        var key = Guid.NewGuid().ToString();

        // All ten can pass the "seen this key?" check before any of them commits.
        // Only the database's primary key on (user_id, key) can make the final decision.
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Customer().SendAsync(OrderRequest(productId, key))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<IdResponse>()));
        Assert.Single(ids.Select(i => i!.Id).Distinct());
        Assert.Equal(1, await OrdersInDatabaseAsync());
    }

    [Fact]
    public async Task SameKey_DifferentBasket_Is422()
    {
        var productId = await CreateProductAsync();
        var key = Guid.NewGuid().ToString();
        await Customer().SendAsync(OrderRequest(productId, key, quantity: 1));

        var response = await Customer().SendAsync(OrderRequest(productId, key, quantity: 5));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, await OrdersInDatabaseAsync());
    }

    [Fact]
    public async Task NoKey_TwoRequests_TwoOrders_HeaderIsOptional()
    {
        var productId = await CreateProductAsync();

        await Customer().SendAsync(OrderRequest(productId, key: null));
        await Customer().SendAsync(OrderRequest(productId, key: null));

        Assert.Equal(2, await OrdersInDatabaseAsync());
    }
}
