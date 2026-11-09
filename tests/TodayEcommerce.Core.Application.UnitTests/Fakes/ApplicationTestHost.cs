using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Events;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Application.UnitTests.Fakes;

/// <summary>
/// Builds the real Application DI setup (MediatR + pipeline behaviors + validators + event handlers)
/// with fake Infrastructure, so tests exercise exactly what the API will run.
/// </summary>
public sealed class ApplicationTestHost
{
    private readonly IServiceProvider _provider;

    public FakeProductRepository Products { get; } = new();
    public FakeOrderRepository Orders { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FixedDateTimeProvider Clock { get; } = new();
    public FakeCacheService Cache { get; } = new();
    public FakeProductSearchService Search { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeIdempotencyStore Idempotency { get; } = new();
    public FakeIntegrationEventPublisher IntegrationEvents { get; } = new();
    public FakeStockReservationRepository Reservations { get; } = new();

    public IMediator Mediator { get; }

    public ApplicationTestHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ICacheService>(Cache);
        services.AddSingleton<IProductSearchService>(Search);
        services.AddSingleton<IProductRepository>(Products);
        services.AddSingleton<IOrderRepository>(Orders);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton<IDateTimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(CurrentUser);
        services.AddSingleton<IIdempotencyStore>(Idempotency);
        services.AddSingleton<IIntegrationEventPublisher>(IntegrationEvents);
        services.AddSingleton<IStockReservationRepository>(Reservations);

        _provider = services.BuildServiceProvider();
        Mediator = _provider.GetRequiredService<IMediator>();
    }

    /// <summary>
    /// Stands in for the outbox job: delivers every pending event (domain events of the in-memory aggregates and
    /// published integration events) to the real handlers — and keeps going while those handlers cause new events,
    /// like OutboxProcessor.DrainAsync. In production this happens asynchronously after the commit.
    /// </summary>
    public Task DispatchDomainEventsAsync() => DrainAsync(Array.Empty<object>());

    /// <summary>Delivers the given events first (e.g. a redelivery, or a deliberately out-of-order message), then drains.</summary>
    public Task DispatchAsync(IEnumerable<object> events) => DrainAsync(events);

    private async Task DrainAsync(IEnumerable<object> first)
    {
        var batch = first.ToList();
        for (var round = 0; round < 20; round++)
        {
            batch.AddRange(CollectPending());
            if (batch.Count == 0)
            {
                return;
            }

            using (var scope = _provider.CreateScope())
            {
                var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
                foreach (var message in batch)
                {
                    await dispatcher.DispatchAsync(message, CancellationToken.None);
                }
            }

            batch = new List<object>();
        }

        throw new InvalidOperationException("Events keep causing events after 20 rounds: a loop?");
    }

    private List<object> CollectPending()
    {
        var aggregates = Products.All.Cast<IHasDomainEvents>().Concat(Orders.Added).Concat(Reservations.All).ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).Cast<object>().ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());
        events.AddRange(IntegrationEvents.TakePending());
        return events;
    }
}
