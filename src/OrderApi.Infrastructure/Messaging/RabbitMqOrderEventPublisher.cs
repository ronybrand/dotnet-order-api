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

    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.QueueDeclareAsync(
            OrderEventMessages.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(
            OrderEventMessages.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: OrderEventMessages.QueueArguments,
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Publishes right after the order commit. A broker outage is only logged: the order status
/// change already committed must never fail because a notification could not be queued
/// (see the README: best-effort publish, not a transactional outbox).
/// </summary>
public sealed partial class RabbitMqOrderEventPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqOrderEventPublisher> logger) : IOrderEventPublisher
{
    public async Task PublishStatusChangedAsync(OrderStatusChangedEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await RabbitMqTopology.CreateFactory(options.Value).CreateConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

            var properties = new BasicProperties { ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: OrderEventMessages.Queue,
                mandatory: false,
                basicProperties: properties,
                body: OrderEventMessages.Serialize(@event),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Never log the customer e-mail (PII): the order id is enough for traceability.
            LogPublishFailed(logger, ex, @event.OrderId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to publish order_status_changed to RabbitMQ: order_id={OrderId}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid orderId);
}
