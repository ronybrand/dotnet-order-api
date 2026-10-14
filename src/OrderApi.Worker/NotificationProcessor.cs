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
    Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

/// <summary>A send failure that no retry can fix (e.g. an invalid recipient): the message goes straight to the DLQ.</summary>
public sealed class PermanentEmailException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed partial class NotificationProcessor(IEmailSender emailSender, ILogger<NotificationProcessor> logger)
{
    public const int MaxRetries = 3;
    private const int RememberedMessages = 10_000;

    private readonly RecentMessageIds _sent = new(RememberedMessages);

    /// <summary>
    /// Decides what to do with a message; retryCount is how many times it was already retried. A messageId already
    /// delivered by e-mail is acknowledged without sending again (redelivery after a lost ack).
    /// </summary>
    public async Task<ProcessingOutcome> ProcessAsync(
        byte[] payload, int retryCount, string? messageId = null, CancellationToken cancellationToken = default)
    {
        if (messageId is not null && _sent.Contains(messageId))
        {
            LogDuplicate(logger, messageId);
            return ProcessingOutcome.Ack;
        }

        var statusChanged = OrderEventMessages.Deserialize(payload);
        if (statusChanged is null)
        {
            LogMalformed(logger);
            return ProcessingOutcome.DeadLetter;
        }

        try
        {
            await emailSender.SendAsync(statusChanged.CustomerEmail, OrderStatusEmailRenderer.Subject(statusChanged), OrderStatusEmailRenderer.Render(statusChanged), cancellationToken);
            // The customer e-mail is PII and is never logged: the order id is enough for tracing.
            LogSent(logger, statusChanged.OrderId, statusChanged.OldStatus, statusChanged.NewStatus);
            if (messageId is not null)
            {
                _sent.Add(messageId);
            }

            return ProcessingOutcome.Ack;
        }
        catch (PermanentEmailException ex)
        {
            LogPermanentFailure(logger, ex, statusChanged.OrderId);
            return ProcessingOutcome.DeadLetter;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (retryCount >= MaxRetries)
            {
                LogGaveUp(logger, ex, statusChanged.OrderId, MaxRetries);
                return ProcessingOutcome.DeadLetter;
            }

            LogRetry(logger, ex, statusChanged.OrderId, retryCount + 1, MaxRetries);
            return ProcessingOutcome.Retry;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate delivery of message_id={MessageId} ignored")]
    private static partial void LogDuplicate(ILogger logger, string messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Malformed order status message, sending to DLQ")]
    private static partial void LogMalformed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "order_status_notification_sent: order_id={OrderId} old_status={OldStatus} new_status={NewStatus}")]
    private static partial void LogSent(ILogger logger, Guid orderId, string oldStatus, string newStatus);

    [LoggerMessage(Level = LogLevel.Error, Message = "Permanent failure sending notification for order_id={OrderId}, sending to DLQ")]
    private static partial void LogPermanentFailure(ILogger logger, Exception exception, Guid orderId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Exceeded {MaxRetries} retries for order_id={OrderId}, sending to DLQ")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid orderId, int maxRetries);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to send notification for order_id={OrderId} (attempt {Attempt}/{MaxRetries}), retrying")]
    private static partial void LogRetry(ILogger logger, Exception exception, Guid orderId, int attempt, int maxRetries);
}
