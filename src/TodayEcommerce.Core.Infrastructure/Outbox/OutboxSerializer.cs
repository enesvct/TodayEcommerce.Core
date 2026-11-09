using System.Diagnostics;
using System.Text.Json;
using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

internal static class OutboxSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage ToMessage(IDomainEvent domainEvent, DateTime utcNow)
    {
        var type = domainEvent.GetType();
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            // No version in the name: deployments must not make old messages unreadable.
            Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Content = JsonSerializer.Serialize(domainEvent, type, JsonOptions),
            OccurredOnUtc = utcNow,
            TraceParent = Activity.Current?.Id, // the current request's (or outbox handler's) trace
        };
    }

    public static IDomainEvent ToDomainEvent(OutboxMessage message)
    {
        var type = System.Type.GetType(message.Type, throwOnError: true)!;
        return (IDomainEvent)(JsonSerializer.Deserialize(message.Content, type, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has empty content."));
    }
}
