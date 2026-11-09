using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Catalog.Search;
using TodayEcommerce.Core.Application.Common.Events;
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

    /// <summary>TraceId that was current when each product was indexed (= inside the outbox handler).</summary>
    public Dictionary<Guid, string?> TraceIdWhenIndexed { get; } = new();

    public Task IndexAsync(ProductSearchDocument document, CancellationToken cancellationToken = default)
    {
        if (IsDown)
        {
            throw new ServiceUnavailableException("search is down", new HttpRequestException("connection refused"));
        }

        Indexed[document.Id] = document;
        TraceIdWhenIndexed[document.Id] = System.Diagnostics.Activity.Current?.TraceId.ToString();
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

// Test-only module messages: Ping's handler publishes Pong, like StockReserved follows OrderPlaced.
public sealed record PingIntegrationEvent(Guid Id) : IIntegrationEvent;
public sealed record PongIntegrationEvent(Guid Id) : IIntegrationEvent;

public sealed class ReceivedIntegrationEvents
{
    public List<object> All { get; } = new();
}

public sealed class PingHandler : IIntegrationEventHandler<PingIntegrationEvent>
{
    private readonly ReceivedIntegrationEvents _received;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;

    public PingHandler(ReceivedIntegrationEvents received, IIntegrationEventPublisher publisher, IUnitOfWork unitOfWork)
    {
        _received = received;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(PingIntegrationEvent e, CancellationToken cancellationToken)
    {
        _received.All.Add(e);
        _publisher.Publish(new PongIntegrationEvent(e.Id));
        await _unitOfWork.SaveChangesAsync(cancellationToken); // Pong commits with Ping's "processed" mark
    }
}

public sealed class PongHandler : IIntegrationEventHandler<PongIntegrationEvent>
{
    private readonly ReceivedIntegrationEvents _received;

    public PongHandler(ReceivedIntegrationEvents received)
    {
        _received = received;
    }

    public Task HandleAsync(PongIntegrationEvent e, CancellationToken cancellationToken)
    {
        _received.All.Add(e);
        return Task.CompletedTask;
    }
}

[Collection(PostgresCollection.Name)]
public class OutboxTests
{
    private readonly PostgresFixture _db;
    private readonly SwitchableSearchService _search = new();
    private readonly ControllableClock _clock = new();
    private readonly ServiceProvider _services;
    private readonly ReceivedIntegrationEvents _received = new();

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
        services.AddSingleton(_received);
        services.AddScoped<IIntegrationEventHandler<PingIntegrationEvent>, PingHandler>();
        services.AddScoped<IIntegrationEventHandler<PongIntegrationEvent>, PongHandler>();
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
        return messages.Single(m => OutboxSerializer.ToEvent(m) is ProductCreatedDomainEvent e && e.ProductId == id);
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
            .Select(OutboxSerializer.ToEvent)
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

        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed

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

    private async Task<Domain.Ordering.OrderId> PlaceOrderAsync(Product product, int quantity)
    {
        var order = Domain.Ordering.Order.Create(new Domain.Ordering.CustomerId(Guid.NewGuid()), "TRY", DateTime.UtcNow);
        order.AddItem(product.Id, product.Name, product.Price, Quantity.Create(quantity));
        order.Place(DateTime.UtcNow);
        await using var db = _db.CreateDbContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private async Task<(Domain.Ordering.OrderStatus Status, int Stock)> StateAsync(Domain.Ordering.OrderId orderId, ProductId productId)
    {
        await using var db = _db.CreateDbContext();
        var status = (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;
        var stock = (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).StockQuantity;
        return (status, stock);
    }

    [Fact]
    public async Task ConfirmedOrderCancelled_ThroughTheRealOutbox_StockComesBack()
    {
        var phone = await CreateProductAsync("Iade Telefonu"); // stock 2
        var orderId = await PlaceOrderAsync(phone, 2);
        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed
        Assert.Equal((Domain.Ordering.OrderStatus.Confirmed, 0), await StateAsync(orderId, phone.Id));

        await using (var db = _db.CreateDbContext())
        {
            (await db.Orders.SingleAsync(o => o.Id == orderId)).Cancel("vazgectim");
            await db.SaveChangesAsync();
        }
        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed

        Assert.Equal((Domain.Ordering.OrderStatus.Cancelled, 2), await StateAsync(orderId, phone.Id));

        // The Catalog's own record, persisted with its items: reserved once, released once.
        await using var check = _db.CreateDbContext();
        var reservation = await check.StockReservations.AsNoTracking().SingleAsync(r => r.OrderId == orderId.Value);
        Assert.Equal(StockReservationStatus.Released, reservation.Status);
        Assert.Equal(2, Assert.Single(reservation.Items).Quantity);
    }

    [Fact]
    public async Task CancelRacingTheReservation_TheLoserGets409_AndARetryEndsCorrectly()
    {
        var phone = await CreateProductAsync("Yaris Telefonu"); // stock 2
        var orderId = await PlaceOrderAsync(phone, 1);

        // The customer's cancel request reads the order while it is still Placed...
        await using var request = _db.CreateDbContext();
        var seenByRequest = await request.Orders.SingleAsync(o => o.Id == orderId);

        // ...meanwhile the outbox reserves stock and confirms the same order.
        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed

        // The request now saves its stale copy: the row version no longer matches → 409, nothing written.
        seenByRequest.Cancel("vazgectim");
        await Assert.ThrowsAsync<ConcurrencyException>(() => request.SaveChangesAsync());
        Assert.Equal((Domain.Ordering.OrderStatus.Confirmed, 1), await StateAsync(orderId, phone.Id));

        // The client retries: it now cancels a Confirmed order, which releases the reserved unit.
        await using (var retry = _db.CreateDbContext())
        {
            (await retry.Orders.SingleAsync(o => o.Id == orderId)).Cancel("vazgectim");
            await retry.SaveChangesAsync();
        }
        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed

        Assert.Equal((Domain.Ordering.OrderStatus.Cancelled, 2), await StateAsync(orderId, phone.Id));
    }

    [Fact]
    public async Task BackgroundWork_ContinuesTheTraceOfTheRequestThatCausedIt()
    {
        // Stand-in for an incoming HTTP request with its own trace.
        using var request = new System.Diagnostics.Activity("POST /api/products").Start();
        var requestTraceId = request.TraceId.ToString();
        var product = await CreateProductAsync("Iz Surulen Urun");
        request.Stop(); // the request is over; the event waits in the outbox

        await Processor.ProcessAsync(batchSize: 100); // later, in a Hangfire job: no request around

        Assert.Equal(requestTraceId, _search.TraceIdWhenIndexed[product.Id.Value]);
    }

    [Fact]
    public async Task TheTraceSurvivesEveryHop_EventsRaisedByHandlersCarryItToo()
    {
        var phone = await CreateProductAsync("Iz Telefonu");
        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed

        using var request = new System.Diagnostics.Activity("POST /api/orders").Start();
        var requestTraceId = request.TraceId.ToString();
        var orderId = await PlaceOrderAsync(phone, 1); // hop 1: OrderPlaced, written by the request
        request.Stop();

        await Processor.DrainAsync(batchSize: 100); // the whole saga: placed → reserved → confirmed // hop 2: reservation raises OrderConfirmed

        await using var db = _db.CreateDbContext();
        var confirmed = (await db.OutboxMessages.AsNoTracking()
                .Where(m => m.Type.Contains("OrderConfirmedDomainEvent"))
                .ToListAsync())
            .Single(m => m.Content.Contains(orderId.Value.ToString()));
        Assert.Contains(requestTraceId, confirmed.TraceParent);
    }

    private async Task PublishAsync(IIntegrationEvent e, bool save)
    {
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(e);
        if (save)
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
    }

    [Fact]
    public async Task IntegrationEvent_TravelsThroughTheOutbox_ToTheOtherModulesHandler()
    {
        var ping = new PingIntegrationEvent(Guid.NewGuid());

        await PublishAsync(ping, save: true);
        await Processor.ProcessAsync(batchSize: 100);

        Assert.Contains(ping, _received.All);
    }

    [Fact]
    public async Task IntegrationEvent_OfAChangeThatWasNeverSaved_IsNeverDelivered()
    {
        var ping = new PingIntegrationEvent(Guid.NewGuid());

        await PublishAsync(ping, save: false); // the use case failed before SaveChanges
        await Processor.DrainAsync(batchSize: 100);

        Assert.DoesNotContain(ping, _received.All);
    }

    [Fact]
    public async Task Drain_DeliversAChainOfEvents_InOneRun_OneTickPerHopWithoutIt()
    {
        var first = new PingIntegrationEvent(Guid.NewGuid());
        await PublishAsync(first, save: true);
        await Processor.ProcessAsync(batchSize: 100);                        // one round: Ping only
        Assert.DoesNotContain(new PongIntegrationEvent(first.Id), _received.All);

        var second = new PingIntegrationEvent(Guid.NewGuid());
        await PublishAsync(second, save: true);
        await Processor.DrainAsync(batchSize: 100);                          // drain: Ping, then the Pong it caused

        Assert.Contains(new PongIntegrationEvent(second.Id), _received.All);
    }
}
