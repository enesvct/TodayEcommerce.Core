using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Application.Common.Exceptions;

namespace TodayEcommerce.Core.Infrastructure.Search;

/// <summary>
/// Elasticsearch adapter over plain HTTP + JSON, so the Query DSL sent is visible as-is.
/// </summary>
internal sealed class ElasticsearchProductSearchService : IProductSearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProductIndex _index;
    private readonly ILogger<ElasticsearchProductSearchService> _logger;

    public ElasticsearchProductSearchService(
        IHttpClientFactory httpClientFactory,
        ProductIndex index,
        ILogger<ElasticsearchProductSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _index = index;
        _logger = logger;
    }

    private HttpClient Client => _httpClientFactory.CreateClient(ProductIndex.HttpClientName);

    public async Task IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            await _index.EnsureCreatedAsync(cancellationToken);

            // PUT with our own id = "insert or replace": indexing the same product twice is harmless (idempotent).
            using var response = await Client.PutAsJsonAsync(
                $"{ProductIndex.Name}/_doc/{document.Id}", document, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)) // = HttpClient timeout
        {
            // Propagate: this runs inside an outbox handler, which will retry the event later.
            _logger.LogWarning(ex, "Failed to index product {ProductId}; outbox will retry", document.Id);
            throw new ServiceUnavailableException("Product search index is temporarily unavailable.", ex);
        }
    }

    public async Task<PagedResult<ProductSearchHit>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var body = BuildQuery(criteria);

        try
        {
            await _index.EnsureCreatedAsync(cancellationToken);

            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await Client.PostAsync($"{ProductIndex.Name}/_search", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken)
                ?? throw new InvalidOperationException("Empty response from Elasticsearch.");

            var hits = json["hits"]!;
            var items = hits["hits"]!.AsArray()
                .Select(h => new ProductSearchHit(
                    h!["_source"].Deserialize<ProductSearchDocument>(JsonOptions)!,
                    h["_score"]?.GetValueKind() == JsonValueKind.Number ? h["_score"]!.GetValue<double>() : null))
                .ToList();

            return new PagedResult<ProductSearchHit>(
                items,
                hits["total"]!["value"]!.GetValue<long>(),
                criteria.Page,
                criteria.PageSize);
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)) // = HttpClient timeout
        {
            // Unlike the cache, search has no fallback: we deliberately don't run LIKE queries on the database.
            throw new ServiceUnavailableException("Product search is temporarily unavailable.", ex);
        }
    }

    /// <summary>
    /// bool query: "must" clauses affect relevance (score); "filter" clauses only include/exclude
    /// and are cacheable inside Elasticsearch, so non-text conditions go there.
    /// </summary>
    private static JsonObject BuildQuery(ProductSearchCriteria criteria)
    {
        var filters = new JsonArray();

        if (criteria.MinPrice.HasValue || criteria.MaxPrice.HasValue)
        {
            var range = new JsonObject();
            if (criteria.MinPrice.HasValue) range["gte"] = criteria.MinPrice.Value;
            if (criteria.MaxPrice.HasValue) range["lte"] = criteria.MaxPrice.Value;
            filters.Add(new JsonObject { ["range"] = new JsonObject { ["price"] = range } });
        }

        if (criteria.InStockOnly)
        {
            filters.Add(new JsonObject { ["term"] = new JsonObject { ["inStock"] = true } });
        }

        var must = new JsonArray();
        if (criteria.Text is not null)
        {
            must.Add(new JsonObject
            {
                ["match"] = new JsonObject
                {
                    ["name"] = new JsonObject
                    {
                        ["query"] = criteria.Text,
                        ["fuzziness"] = "AUTO", // tolerate typos: "telefn" still finds "telefon"
                        ["operator"] = "and",   // every word must match (after stemming / fuzziness)
                    },
                },
            });
        }
        else
        {
            must.Add(new JsonObject { ["match_all"] = new JsonObject() });
        }

        // With a search text, order by relevance; without one, a stable alphabetical order.
        var sort = criteria.Text is not null
            ? new JsonArray("_score", new JsonObject { ["name.keyword"] = "asc" })
            : new JsonArray(new JsonObject { ["name.keyword"] = "asc" });

        return new JsonObject
        {
            ["from"] = (criteria.Page - 1) * criteria.PageSize,
            ["size"] = criteria.PageSize,
            ["track_scores"] = true,
            ["query"] = new JsonObject
            {
                ["bool"] = new JsonObject { ["must"] = must, ["filter"] = filters },
            },
            ["sort"] = sort,
        };
    }
}
