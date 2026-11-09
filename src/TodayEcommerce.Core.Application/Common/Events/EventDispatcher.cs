using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Application.Common.Events;

public interface IEventDispatcher
{
    /// <summary>
    /// Runs every registered handler for the message's concrete type: IDomainEventHandler&lt;T&gt; for a domain
    /// event, IIntegrationEventHandler&lt;T&gt; for an integration event. Exceptions propagate (→ retry).
    /// </summary>
    Task DispatchAsync(object message, CancellationToken cancellationToken);
}

/// <summary>Used by the outbox processor (Infrastructure) and by tests to dispatch synchronously.</summary>
internal sealed class EventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public EventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(object message, CancellationToken cancellationToken)
    {
        var handlerDefinition = message switch
        {
            IDomainEvent => typeof(IDomainEventHandler<>),
            IIntegrationEvent => typeof(IIntegrationEventHandler<>),
            _ => throw new ArgumentException($"{message.GetType().Name} is neither a domain nor an integration event.", nameof(message)),
        };
        var handlerType = handlerDefinition.MakeGenericType(message.GetType());

        foreach (var handler in _serviceProvider.GetServices(handlerType))
        {
            // dynamic picks HandleAsync(TEvent, ...) for the concrete event type at runtime.
            await ((dynamic)handler!).HandleAsync((dynamic)message, cancellationToken);
        }
    }
}
