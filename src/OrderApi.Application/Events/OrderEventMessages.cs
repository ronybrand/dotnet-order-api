using System.Text.Json;

namespace OrderApi.Application.Events;

/// <summary>Wire contract of the order status notification queue (shared by API and worker).</summary>
public static class OrderEventMessages
{
    public const string Queue = "order.status.changed";
    public const string DeadLetterQueue = "order.status.changed.dlq";

    /// <summary>
    /// Queue arguments must be identical on every declaration, otherwise RabbitMQ answers
    /// 406 PRECONDITION_FAILED; both producer and consumer use this single definition.
    /// </summary>
    public static IDictionary<string, object?> QueueArguments { get; } = new Dictionary<string, object?>
    {
        ["x-dead-letter-exchange"] = string.Empty,
        ["x-dead-letter-routing-key"] = DeadLetterQueue,
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static byte[] Serialize(OrderStatusChangedEvent @event) => JsonSerializer.SerializeToUtf8Bytes(@event, JsonOptions);

    /// <summary>Returns null for malformed JSON or a payload missing required fields.</summary>
    public static OrderStatusChangedEvent? Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            var @event = JsonSerializer.Deserialize<OrderStatusChangedEvent>(payload, JsonOptions);
            return @event is { OrderId: var id }
                && id != Guid.Empty
                && !string.IsNullOrWhiteSpace(@event.CustomerEmail)
                && !string.IsNullOrWhiteSpace(@event.CustomerName)
                && !string.IsNullOrWhiteSpace(@event.OldStatus)
                && !string.IsNullOrWhiteSpace(@event.NewStatus)
                    ? @event
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
}
