using Microsoft.Extensions.Logging;
using OrderApi.Application.Events;

namespace OrderApi.Worker;

public enum ProcessingOutcome
{
    Ack,
    Retry,
    DeadLetter,
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public sealed partial class NotificationProcessor(IEmailSender emailSender, ILogger<NotificationProcessor> logger)
{
    public const int MaxRetries = 3;

    /// <summary>Decides what to do with a message; retryCount is how many times it was already retried.</summary>
    public async Task<ProcessingOutcome> ProcessAsync(byte[] payload, int retryCount, CancellationToken cancellationToken = default)
    {
        var @event = OrderEventMessages.Deserialize(payload);
        if (@event is null)
        {
            LogMalformed(logger);
            return ProcessingOutcome.DeadLetter;
        }

        try
        {
            await emailSender.SendAsync(@event.CustomerEmail, OrderStatusEmailRenderer.Subject(@event), OrderStatusEmailRenderer.Render(@event), cancellationToken);
            // The customer e-mail is PII and is never logged: the order id is enough for tracing.
            LogSent(logger, @event.OrderId, @event.OldStatus, @event.NewStatus);
            return ProcessingOutcome.Ack;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (retryCount >= MaxRetries)
            {
                LogGaveUp(logger, ex, @event.OrderId, MaxRetries);
                return ProcessingOutcome.DeadLetter;
            }

            LogRetry(logger, ex, @event.OrderId, retryCount + 1, MaxRetries);
            return ProcessingOutcome.Retry;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Malformed order status message, sending to DLQ")]
    private static partial void LogMalformed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "order_status_notification_sent: order_id={OrderId} old_status={OldStatus} new_status={NewStatus}")]
    private static partial void LogSent(ILogger logger, Guid orderId, string oldStatus, string newStatus);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceeded {MaxRetries} retries for order_id={OrderId}, sending to DLQ")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid orderId, int maxRetries);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send notification for order_id={OrderId} (attempt {Attempt}/{MaxRetries}), retrying")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid orderId, int attempt, int maxRetries);
}
