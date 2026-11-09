using System.Diagnostics;
using System.Text.Json;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

internal static class OutboxSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Domain events and integration events travel the same way: as rows in the outbox.</summary>
    public static OutboxMessage ToMessage(object @event, DateTime utcNow)
    {
        var type = @event.GetType();
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            // No version in the name: deployments must not make old messages unreadable.
            Type = $"{type.FullName}, {type.Assembly.GetName().Name}",
            Content = JsonSerializer.Serialize(@event, type, JsonOptions),
            OccurredOnUtc = utcNow,
            TraceParent = Activity.Current?.Id, // the current request's (or outbox handler's) trace
        };
    }

    public static object ToEvent(OutboxMessage message)
    {
        var type = System.Type.GetType(message.Type, throwOnError: true)!;
        return JsonSerializer.Deserialize(message.Content, type, JsonOptions)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has empty content.");
    }
}
