namespace OrderApi.Application.Events;

public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    string CustomerEmail,
    string CustomerName,
    string OldStatus,
    string NewStatus,
    decimal TotalAmount,
    DateTimeOffset ChangedAt);

public interface IOrderEventPublisher
{
    /// <summary>Best-effort publish after the commit; implementations must not throw for broker failures.</summary>
    Task PublishStatusChangedAsync(OrderStatusChangedEvent statusChanged, CancellationToken cancellationToken = default);
}
