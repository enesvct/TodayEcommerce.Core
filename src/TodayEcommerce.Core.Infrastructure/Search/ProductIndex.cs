using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TodayEcommerce.Core.Infrastructure.Search;

/// <summary>
/// Owns the "products" index: its name and mapping, and creating it before first use.
/// </summary>
/// <remarks>
/// The name is versioned like the cache keys: a mapping change means creating "products-v2",
/// reindexing into it and switching over — existing field mappings can't be changed in place.
/// </remarks>
internal sealed class ProductIndex
{
    public const string HttpClientName = "elasticsearch";
    public const string Name = "products-v1";

    /// <summary>
    /// Explicit mapping instead of Elasticsearch guessing types from the first document:
    /// - name: analysed with the built-in Turkish analyzer (Turkish lowercasing İ/ı, stemming "telefonlar" → "telefon"),
    ///   plus an exact "keyword" sub-field for sorting.
    /// - price: scaled_float stores cents exactly (no floating-point drift) and supports range filters.
    /// - currency / id: keyword = exact values, not analysed text.
    /// </summary>
    private const string Definition = """
        {
          "settings": { "number_of_shards": 1, "number_of_replicas": 0 },
          "mappings": {
            "dynamic": "strict",
            "properties": {
              "id":            { "type": "keyword" },
              "name":          { "type": "text", "analyzer": "turkish",
                                 "fields": { "keyword": { "type": "keyword", "ignore_above": 256 } } },
              "price":         { "type": "scaled_float", "scaling_factor": 100 },
              "currency":      { "type": "keyword" },
              "stockQuantity": { "type": "integer" },
              "inStock":       { "type": "boolean" }
            }
          }
        }
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProductIndex> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _ready;

    public ProductIndex(IHttpClientFactory httpClientFactory, ILogger<ProductIndex> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Makes sure the index exists with our mapping. Returns true if it was created by this call
    /// (so the caller knows a backfill from the database is needed).
    /// </summary>
    public async Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return false;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
            {
                return false;
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var head = new HttpRequestMessage(HttpMethod.Head, Name);
            using var exists = await client.SendAsync(head, cancellationToken);

            var created = false;
            if (exists.StatusCode == HttpStatusCode.NotFound)
            {
                using var body = new StringContent(Definition, Encoding.UTF8, "application/json");
                using var response = await client.PutAsync(Name, body, cancellationToken);
                response.EnsureSuccessStatusCode();
                created = true;
                _logger.LogInformation("Created Elasticsearch index {Index}", Name);
            }
            else
            {
                exists.EnsureSuccessStatusCode();
            }

            _ready = true;
            return created;
        }
        finally
        {
            _lock.Release();
        }
    }
}
