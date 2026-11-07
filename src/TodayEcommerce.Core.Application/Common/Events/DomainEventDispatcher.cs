using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Application.Common.Events;

public interface IDomainEventDispatcher
{
    /// <summary>Runs every registered handler for the event's concrete type. Exceptions propagate (→ retry).</summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Finds IDomainEventHandler&lt;TEvent&gt; implementations for the runtime type of the event.
/// Used by the outbox processor (Infrastructure) and by tests to dispatch synchronously.
/// </summary>
internal sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public DomainEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

        foreach (var handler in _serviceProvider.GetServices(handlerType))
        {
            // dynamic picks HandleAsync(TEvent, ...) for the concrete event type at runtime.
            await ((dynamic)handler!).HandleAsync((dynamic)domainEvent, cancellationToken);
        }
    }
}
