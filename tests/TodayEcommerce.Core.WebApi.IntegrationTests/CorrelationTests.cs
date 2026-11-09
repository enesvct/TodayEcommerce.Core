using System.Net;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CorrelationTests : IDisposable
{
    private readonly ApiFactory _api;

    public CorrelationTests(PostgresFixture db)
    {
        _api = new ApiFactory(db.ConnectionString, "Testing");
    }

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task EveryResponse_CarriesACorrelationId_AlsoErrors()
    {
        var response = await _api.CreateClient().GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.Matches("^[0-9a-f]{32}$", correlationId);
        Assert.Contains(correlationId, await response.Content.ReadAsStringAsync()); // same id as ProblemDetails.traceId
    }

    [Fact]
    public async Task IncomingW3CTraceContext_IsContinued_NotReplaced()
    {
        // A gateway or another service already started a trace: we join it instead of starting a new one.
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        var response = await _api.CreateClient().SendAsync(request);

        Assert.Equal(traceId, Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
    }
}
