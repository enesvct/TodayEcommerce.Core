using System.Net;
using System.Net.Http.Json;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>
/// POST /dev/token mints tokens for anyone who asks. Convenient in Development — and a full
/// authentication bypass anywhere else. These tests pin down that it only exists in Development.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DevTokenEndpointTests
{
    private readonly PostgresFixture _db;

    public DevTokenEndpointTests(PostgresFixture db)
    {
        _db = db;
    }

    private sealed record TokenResponse(string AccessToken);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task OutsideDevelopment_TheEndpointDoesNotExist(string environment)
    {
        using var api = new ApiFactory(_db.ConnectionString, environment);

        var response = await api.CreateClient().PostAsJsonAsync("/dev/token", new { user = "admin" });

        // 401, not 404: the fallback policy (secure by default) also covers routes that don't exist,
        // so an anonymous caller cannot even tell whether /dev/token is there.
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected no /dev/token in {environment}, got {(int)response.StatusCode}");
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InDevelopment_IssuesATokenThatTheApiAccepts()
    {
        using var api = new ApiFactory(_db.ConnectionString, "Development");
        var client = api.CreateClient();

        var tokenResponse = await client.PostAsJsonAsync("/dev/token", new { user = "admin" });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        var create = await client.As(token).PostAsJsonAsync("/api/products",
            new { name = "Dev Token Urunu", price = 10m, currency = "TRY", initialStock = 1 });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }
}
