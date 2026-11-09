using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Application.Common;
using TodayEcommerce.Core.Application;
using TodayEcommerce.Core.Domain.Catalog.Events;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.SharedKernel;
using TodayEcommerce.Core.Infrastructure.Outbox;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.IntegrationTests;

/// <summary>Search engine stand-in that can be switched off, like stopping the Elasticsearch container.</summary>
public sealed class SwitchableSearchService : IProductSearchService
{
    public bool IsDown { get; set; }
    public Dictionary<Guid, ProductSearchDocument> Indexed { get; } = new();

    public Task IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        if (IsDown)
        {
            throw new ServiceUnavailableException("search is down", new HttpRequestException("connection refused"));
        }

        Indexed[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task<PagedResult<ProductSearchHit>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>A clock the test can move forward, so hours of back-off take no real time.</summary>
public sealed class ControllableClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class NoOpCache : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

[Collection(PostgresCollection.Name)]
public class OutboxTests
{
    private readonly PostgresFixture _db;
    private readonly SwitchableSearchService _search = new();
    private readonly ControllableClock _clock = new();
    private readonly ServiceProvider _services;

    public OutboxTests(PostgresFixture db)
    {
        _db = db;

        // The real composition: AddApplication + AddInfrastructure against the test database.
        // Only the external systems (search, cache) are replaced by controllable fakes.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = db.ConnectionString,
                ["ConnectionStrings:Redis"] = "localhost:1",
                ["ConnectionStrings:Elasticsearch"] = "http://localhost:1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddSingleton<IProductSearchService>(_search);
        services.AddSingleton<ICacheService, NoOpCache>();
        services.AddSingleton<IDateTimeProvider>(_clock);
        _services = services.BuildServiceProvider();
    }

    private OutboxProcessor Processor => _services.GetRequiredService<OutboxProcessor>();

    private async Task<Product> CreateProductAsync(string name)
    {
        var product = Product.Create(name, Money.Create(7_000m, "TRY"), 2);
        await using var db = _db.CreateDbContext();
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private async Task<OutboxMessage> OutboxMessageForAsync(ProductId id)
    {
        await using var db = _db.CreateDbContext();
        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type.Contains(nameof(ProductCreatedDomainEvent)))
            .ToListAsync();
        return messages.Single(m => OutboxSerializer.ToDomainEvent(m) is ProductCreatedDomainEvent e && e.ProductId == id);
    }

    [Fact]
    public async Task SavingAnAggregate_WritesItsEventToTheOutbox_InTheSameCommit()
    {
        var product = await CreateProductAsync("Tablet Q");

        var message = await OutboxMessageForAsync(product.Id);
        Assert.Null(message.ProcessedOnUtc);
        Assert.Contains(product.Id.Value.ToString(), message.Content);
        Assert.Empty(product.DomainEvents); // cleared after the successful commit
    }

    [Fact]
    public async Task FailedSave_WritesNoOutboxMessage_NoPhantomEvents()
    {
        var product = await CreateProductAsync("Telefon X");

        await using var first = _db.CreateDbContext();
        await using var second = _db.CreateDbContext();
        var a = await first.Products.SingleAsync(p => p.Id == product.Id);
        var b = await second.Products.SingleAsync(p => p.Id == product.Id);
        a.ChangePrice(Money.Create(8_000m, "TRY"));
        b.ChangePrice(Money.Create(9_000m, "TRY"));
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = _db.CreateDbContext();
        var priceEvents = (await check.OutboxMessages.AsNoTracking()
                .Where(m => m.Type.Contains(nameof(ProductPriceChangedDomainEvent)))
                .ToListAsync())
            .Select(OutboxSerializer.ToDomainEvent)
            .OfType<ProductPriceChangedDomainEvent>()
            .Where(e => e.ProductId == product.Id)
            .ToList();

        // Only the committed change produced an event; the rejected 9.000 never "happened".
        Assert.Equal(8_000m, Assert.Single(priceEvents).NewAmount);
    }

    [Fact]
    public async Task TabletQ_SearchIsDown_EventWaitsInOutbox_AndIsIndexedOnceSearchRecovers()
    {
        var product = await CreateProductAsync("Tablet Q");

        // 1. Elasticsearch is down while the processor runs.
        _search.IsDown = true;
        await Processor.ProcessAsync(batchSize: 100);

        var afterFailure = await OutboxMessageForAsync(product.Id);
        Assert.Null(afterFailure.ProcessedOnUtc);      // not lost: still pending
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.Contains("ServiceUnavailableException", afterFailure.LastError);
        Assert.False(_search.Indexed.ContainsKey(product.Id.Value));

        // 2. Elasticsearch is back; once the back-off delay has passed, the same message is delivered.
        _search.IsDown = false;
        _clock.Advance(OutboxProcessor.DelayAfter(1));
        await Processor.ProcessAsync(batchSize: 100);

        var afterRecovery = await OutboxMessageForAsync(product.Id);
        Assert.NotNull(afterRecovery.ProcessedOnUtc);
        Assert.Null(afterRecovery.LastError);
        Assert.Equal("Tablet Q", _search.Indexed[product.Id.Value].Name);
    }

    [Fact]
    public async Task ProcessedMessages_AreNotDeliveredAgain()
    {
        var product = await CreateProductAsync("Kulaklik Y");
        await Processor.ProcessAsync(batchSize: 100);
        _search.Indexed.Clear();

        await Processor.ProcessAsync(batchSize: 100);

        Assert.False(_search.Indexed.ContainsKey(product.Id.Value));
    }

    [Fact]
    public async Task LastUnit_TwoPlacedOrders_ThroughTheRealOutbox_OneConfirmedOneCancelled()
    {
        var phone = await CreateProductAsync("Son Telefon");
        await using (var db = _db.CreateDbContext())
        {
            (await db.Products.SingleAsync(p => p.Id == phone.Id)).RemoveStock(Quantity.Create(1)); // stock 2 → 1
            await db.SaveChangesAsync();
        }

        var orderIds = new List<Domain.Ordering.OrderId>();
        foreach (var _ in new[] { "Ayse", "Mehmet" })
        {
            var order = Domain.Ordering.Order.Create(new Domain.Ordering.CustomerId(Guid.NewGuid()), "TRY", DateTime.UtcNow);
            order.AddItem(phone.Id, phone.Name, phone.Price, Quantity.Create(1));
            order.Place(DateTime.UtcNow);
            await using var db = _db.CreateDbContext();
            db.Orders.Add(order);
            await db.SaveChangesAsync(); // order + OrderPlaced outbox row, one commit
            orderIds.Add(order.Id);
        }

        await Processor.ProcessAsync(batchSize: 100);

        await using var check = _db.CreateDbContext();
        var statuses = await check.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .OrderBy(o => o.PlacedAtUtc)
            .Select(o => o.Status)
            .ToListAsync();
        Assert.Equal(new[] { Domain.Ordering.OrderStatus.Confirmed, Domain.Ordering.OrderStatus.Cancelled }, statuses);
        Assert.Equal(0, (await check.Products.AsNoTracking().SingleAsync(p => p.Id == phone.Id)).StockQuantity);
    }

    [Fact]
    public async Task FailedMessage_IsNotRetriedBeforeItsBackoffDelay()
    {
        var product = await CreateProductAsync("Sabirli Urun");
        _search.IsDown = true;
        await Processor.ProcessAsync(batchSize: 100); // attempt 1 fails → wait 5s

        await Processor.ProcessAsync(batchSize: 100); // runs again immediately (like the 5s Hangfire tick)
        Assert.Equal(1, (await OutboxMessageForAsync(product.Id)).AttemptCount); // skipped: too early

        _clock.Advance(OutboxProcessor.DelayAfter(1));
        await Processor.ProcessAsync(batchSize: 100);
        var message = await OutboxMessageForAsync(product.Id);
        Assert.Equal(2, message.AttemptCount);                                     // retried now
        Assert.Equal(_clock.UtcNow + OutboxProcessor.DelayAfter(2), message.NextAttemptOnUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task SurvivesAThirtySecondOutage_TheBugFoundInTheLiveDemo()
    {
        var product = await CreateProductAsync("Kamera K");
        _search.IsDown = true;

        // Hangfire ticks every 5s for 30s while Elasticsearch restarts.
        for (var second = 0; second < 30; second += 5)
        {
            await Processor.ProcessAsync(batchSize: 100);
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        _search.IsDown = false;
        for (var second = 0; second < 60; second += 5)
        {
            await Processor.ProcessAsync(batchSize: 100);
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        var message = await OutboxMessageForAsync(product.Id);
        Assert.NotNull(message.ProcessedOnUtc);   // with 5 fixed retries this message had already been given up
        Assert.True(message.AttemptCount < 5);    // back-off spent few attempts during the outage
        Assert.True(_search.Indexed.ContainsKey(product.Id.Value));
    }

    [Fact]
    public async Task PoisonMessage_StopsBeingPickedUp_AfterMaxAttempts()
    {
        var product = await CreateProductAsync("Bozuk Urun");
        _search.IsDown = true;

        for (var i = 0; i < OutboxProcessor.MaxAttempts + 2; i++)
        {
            await Processor.ProcessAsync(batchSize: 100);
            _clock.Advance(TimeSpan.FromHours(2)); // longer than any back-off step
        }

        var message = await OutboxMessageForAsync(product.Id);
        Assert.Equal(OutboxProcessor.MaxAttempts, message.AttemptCount); // capped: no endless retry loop
        Assert.Null(message.ProcessedOnUtc);                            // left for a human to inspect
    }
}
