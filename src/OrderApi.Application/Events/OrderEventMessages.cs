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

    public static byte[] Serialize(OrderStatusChangedEvent statusChanged) => JsonSerializer.SerializeToUtf8Bytes(statusChanged, JsonOptions);

    /// <summary>Returns null for malformed JSON or a payload missing required fields.</summary>
    public static OrderStatusChangedEvent? Deserialize(ReadOnlySpan<byte> payload)
    {
        try
        {
            var statusChanged = JsonSerializer.Deserialize<OrderStatusChangedEvent>(payload, JsonOptions);
            return statusChanged is { OrderId: var id }
                && id != Guid.Empty
                && !string.IsNullOrWhiteSpace(statusChanged.CustomerEmail)
                && !string.IsNullOrWhiteSpace(statusChanged.CustomerName)
                && !string.IsNullOrWhiteSpace(statusChanged.OldStatus)
                && !string.IsNullOrWhiteSpace(statusChanged.NewStatus)
                    ? statusChanged
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

    // No credential defaults: a deployment that forgets them must fail at startup, not fall back to guest/guest.
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string VirtualHost { get; set; } = "/";

    /// <summary>The queue carries customer e-mail and name, so the broker connection is TLS unless AllowInsecure.</summary>
    public bool UseTls { get; set; }

    /// <summary>Plain-text broker connection for local development only (set in appsettings.Development.json).</summary>
    public bool AllowInsecure { get; set; }
}
