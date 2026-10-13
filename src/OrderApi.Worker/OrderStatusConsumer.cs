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
        var factory = RabbitMqTopology.CreateFactory(options.Value);
        factory.DispatchConsumersAsync = true;
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        RabbitMqTopology.Declare(channel);
        channel.BasicQos(0, 10, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += (_, delivery) => HandleAsync(channel, delivery, stoppingToken);
        channel.BasicConsume(OrderEventMessages.Queue, autoAck: false, consumer);
        LogStarted(logger, OrderEventMessages.Queue);

        var shutdown = new TaskCompletionSource();
        connection.ConnectionShutdown += (_, _) => shutdown.TrySetResult();
        await using var registration = stoppingToken.Register(() => shutdown.TrySetResult());
        await shutdown.Task;
        stoppingToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("RabbitMQ connection closed");
    }

    private async Task HandleAsync(IModel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var retryCount = RetryCountOf(delivery.BasicProperties);
        var body = delivery.Body.ToArray();
        var outcome = await processor.ProcessAsync(body, retryCount, cancellationToken);

        switch (outcome)
        {
            case ProcessingOutcome.Ack:
                channel.BasicAck(delivery.DeliveryTag, false);
                break;
            case ProcessingOutcome.Retry:
                await Task.Delay(RetryBackoff, cancellationToken);
                var properties = channel.CreateBasicProperties();
                properties.ContentType = "application/json";
                properties.Persistent = true;
                properties.Headers = new Dictionary<string, object> { [RetryHeader] = retryCount + 1 };
                channel.BasicPublish(string.Empty, OrderEventMessages.Queue, properties, body);
                channel.BasicAck(delivery.DeliveryTag, false);
                break;
            default:
                // requeue=false routes the message to the dead-letter queue configured on the queue.
                channel.BasicNack(delivery.DeliveryTag, false, false);
                break;
        }
    }

    private static int RetryCountOf(IBasicProperties properties) =>
        properties.Headers is not null && properties.Headers.TryGetValue(RetryHeader, out var value) && value is int count ? count : 0;

    [LoggerMessage(Level = LogLevel.Information, Message = "worker_started: queue={Queue}")]
    private static partial void LogStarted(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Error, Message = "RabbitMQ connection lost, retrying in {Seconds}s")]
    private static partial void LogConnectionLost(ILogger logger, Exception exception, double seconds);
}
