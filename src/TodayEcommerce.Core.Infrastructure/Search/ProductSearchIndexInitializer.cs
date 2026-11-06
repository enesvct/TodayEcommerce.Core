using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.Search;

/// <summary>
/// On startup: creates the index if missing and, when it was just created, fills it from the
/// database (the source of truth). This is also how the index is rebuilt: delete it and restart.
/// Never blocks or crashes startup — if Elasticsearch is down, the app runs without search.
/// </summary>
internal sealed class ProductSearchIndexInitializer : BackgroundService
{
    private const int BatchSize = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProductIndex _index;
    private readonly ILogger<ProductSearchIndexInitializer> _logger;

    public ProductSearchIndexInitializer(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ProductIndex index,
        ILogger<ProductSearchIndexInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _index = index;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await _index.EnsureCreatedAsync(stoppingToken))
            {
                return; // index already existed; it is kept in sync by the write path
            }

            var total = await BackfillAsync(stoppingToken);
            _logger.LogInformation("Backfilled {Count} products into {Index}", total, ProductIndex.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Elasticsearch initialisation failed; search is unavailable until it recovers");
        }
    }

    private async Task<int> BackfillAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = _httpClientFactory.CreateClient(ProductIndex.HttpClientName);

        var total = 0;
        while (true)
        {
            var batch = await db.Products.AsNoTracking()
                .OrderBy(p => p.Id)
                .Skip(total)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                return total;
            }

            // _bulk API: many documents in one HTTP call (newline-delimited JSON: action line, then document line).
            var ndjson = new StringBuilder();
            foreach (var product in batch)
            {
                var document = ProductSearchDocument.From(product);
                ndjson.Append("{\"index\":{\"_id\":\"").Append(document.Id).Append("\"}}\n");
                ndjson.Append(JsonSerializer.Serialize(document, JsonOptions)).Append('\n');
            }

            using var content = new StringContent(ndjson.ToString(), Encoding.UTF8, "application/x-ndjson");
            using var response = await client.PostAsync($"{ProductIndex.Name}/_bulk", content, cancellationToken);
            response.EnsureSuccessStatusCode();

            total += batch.Count;
        }
    }
}
