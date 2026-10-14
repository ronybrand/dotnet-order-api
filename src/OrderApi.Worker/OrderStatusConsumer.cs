using Microsoft.Extensions.Options;
using OrderApi.Application.Events;
using OrderApi.Infrastructure.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderApi.Worker;

/// <summary>Long-running consumer of order.status.changed; reconnects forever if the broker drops.</summary>
public sealed partial class OrderStatusConsumer(
    IOptions<RabbitMqOptions> options,
    NotificationProcessor processor,
    ILogger<OrderStatusConsumer> logger) : BackgroundService
{
    private const string RetryHeader = "x-retry-count";
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryBackoff = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LogConnectionLost(logger, ex, ReconnectDelay.TotalSeconds);
                await Task.Delay(ReconnectDelay, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await using var connection = await RabbitMqTopology.CreateFactory(options.Value).CreateConnectionAsync(stoppingToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 10, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
        await channel.BasicConsumeAsync(OrderEventMessages.Queue, autoAck: false, consumer, stoppingToken);
        LogStarted(logger, OrderEventMessages.Queue);

        var shutdown = new TaskCompletionSource();
        connection.ConnectionShutdownAsync += (_, _) =>
        {
            shutdown.TrySetResult();
            return Task.CompletedTask;
        };
        await using var registration = stoppingToken.Register(() => shutdown.TrySetResult());
        await shutdown.Task;
        stoppingToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("RabbitMQ connection closed");
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var retryCount = RetryCountOf(delivery.BasicProperties);
        var body = delivery.Body.ToArray();
        var outcome = await processor.ProcessAsync(body, retryCount, cancellationToken);

        switch (outcome)
        {
            case ProcessingOutcome.Ack:
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                break;
            case ProcessingOutcome.Retry:
                await Task.Delay(RetryBackoff, cancellationToken);
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    Headers = new Dictionary<string, object?> { [RetryHeader] = retryCount + 1 },
                };
                await channel.BasicPublishAsync(string.Empty, OrderEventMessages.Queue, false, properties, body, cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                break;
            case ProcessingOutcome.DeadLetter:
            default:
                // requeue=false routes the message to the dead-letter queue configured on the queue.
                await channel.BasicNackAsync(delivery.DeliveryTag, false, false, cancellationToken);
                break;
        }
    }

    private static int RetryCountOf(IReadOnlyBasicProperties properties) =>
        properties.Headers is not null && properties.Headers.TryGetValue(RetryHeader, out var value) && value is int count ? count : 0;

    [LoggerMessage(Level = LogLevel.Information, Message = "worker_started: queue={Queue}")]
    private static partial void LogStarted(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ connection lost, retrying in {Seconds}s")]
    private static partial void LogConnectionLost(ILogger logger, Exception exception, double seconds);
}
