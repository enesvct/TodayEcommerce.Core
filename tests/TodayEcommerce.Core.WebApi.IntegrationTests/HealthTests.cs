using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>
/// In these tests Redis and Elasticsearch point at closed ports (see ApiFactory) — i.e. they are "down".
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HealthTests
{
    private readonly PostgresFixture _db;

    public HealthTests(PostgresFixture db)
    {
        _db = db;
    }

    private sealed record Check(string Name, string Status);
    private sealed record Report(string Status, List<Check> Checks);

    [Fact]
    public async Task Live_IsAnonymous_AndChecksNoDependency()
    {
        using var api = new ApiFactory(_db.ConnectionString, "Testing");

        var response = await api.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // no token needed: the prober has none
        Assert.Empty((await response.Content.ReadFromJsonAsync<Report>())!.Checks);
    }

    [Fact]
    public async Task Ready_SearchAndCacheDown_IsDegradedButStill200_KeepServingOrders()
    {
        using var api = new ApiFactory(_db.ConnectionString, "Testing");
        var client = api.CreateClient();
        await client.GetAsync("/health/live"); // start the app first, so only the probe is timed

        var stopwatch = Stopwatch.StartNew();
        var response = await client.GetAsync("/health/ready");
        stopwatch.Stop();
        var report = await response.Content.ReadFromJsonAsync<Report>();

        // Each check gives up after 2s. Without WaitAsync this took 15+ s: a prober would have
        // declared the instance dead although it was serving orders just fine.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Probe took {stopwatch.Elapsed.TotalSeconds:0.0}s");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", report!.Status);
        Assert.Equal("Healthy", report.Checks.Single(c => c.Name == "postgresql").Status);
        Assert.Equal("Degraded", report.Checks.Single(c => c.Name == "elasticsearch").Status);
        Assert.Equal("Degraded", report.Checks.Single(c => c.Name == "redis").Status);
    }

    [Fact]
    public async Task Ready_DatabaseDown_Is503_TakeThisInstanceOutOfRotation()
    {
        using var api = new ApiFactory("Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=1", "Testing");

        var response = await api.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"name\":\"postgresql\",\"status\":\"Unhealthy\"", body);
        Assert.DoesNotContain("Port=1", body);       // no connection details leak through a public endpoint
        Assert.DoesNotContain("Exception", body);
    }
}
