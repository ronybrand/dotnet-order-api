using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderApi.Application.Events;
using RabbitMQ.Client;

namespace OrderApi.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public static ConnectionFactory CreateFactory(RabbitMqOptions options) => new()
    {
        HostName = options.Host,
        Port = options.Port,
        UserName = options.UserName,
        Password = options.Password,
        VirtualHost = options.VirtualHost,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
    };

    public static void Declare(IModel channel)
    {
        channel.QueueDeclare(OrderEventMessages.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueDeclare(
            OrderEventMessages.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: OrderEventMessages.QueueArguments);
    }
}

/// <summary>
/// Publishes right after the order commit. A broker outage is only logged: the order status
/// change already committed must never fail because a notification could not be queued
/// (see ADR 0002: best-effort publish, not a transactional outbox).
/// </summary>
public sealed partial class RabbitMqOrderEventPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqOrderEventPublisher> logger) : IOrderEventPublisher
{
    public Task PublishStatusChangedAsync(OrderStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = RabbitMqTopology.CreateFactory(options.Value).CreateConnection();
            using var channel = connection.CreateModel();
            RabbitMqTopology.Declare(channel);

            var properties = channel.CreateBasicProperties();
            properties.ContentType = "application/json";
            properties.Persistent = true;
            channel.BasicPublish(string.Empty, OrderEventMessages.Queue, properties, OrderEventMessages.Serialize(@event));
        }
        catch (Exception ex)
        {
            // Never log the customer e-mail (PII): the order id is enough for traceability.
            LogPublishFailed(logger, ex, @event.OrderId);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish order_status_changed to RabbitMQ: order_id={OrderId}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid orderId);
}
