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

        _provider = services.BuildServiceProvider();
        Mediator = _provider.GetRequiredService<IMediator>();
    }

    /// <summary>
    /// Stands in for "outbox → processor": dispatches every pending event of the in-memory aggregates
    /// to the real handlers, synchronously. In production this happens asynchronously after the commit.
    /// </summary>
    public async Task DispatchDomainEventsAsync()
    {
        var aggregates = Products.All.Cast<IHasDomainEvents>().Concat(Orders.Added).ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());

        await DispatchAsync(events);
    }

    /// <summary>Dispatches the given events (e.g. to simulate the outbox delivering an event twice).</summary>
    public async Task DispatchAsync(IEnumerable<IDomainEvent> events)
    {
        using var scope = _provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
        foreach (var domainEvent in events)
        {
            await dispatcher.DispatchAsync(domainEvent, CancellationToken.None);
        }
    }
}
