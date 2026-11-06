using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Application.UnitTests.Fakes;

/// <summary>
/// Builds the real Application DI setup (MediatR + pipeline behaviors + validators)
/// with fake Infrastructure, so tests exercise exactly what the API will run.
/// </summary>
public sealed class ApplicationTestHost
{
    public FakeProductRepository Products { get; } = new();
    public FakeOrderRepository Orders { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FixedDateTimeProvider Clock { get; } = new();

    public IMediator Mediator { get; }

    public ApplicationTestHost()
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<IProductRepository>(Products);
        services.AddSingleton<IOrderRepository>(Orders);
        services.AddSingleton<IUnitOfWork>(UnitOfWork);
        services.AddSingleton<IDateTimeProvider>(Clock);

        Mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
    }
}
