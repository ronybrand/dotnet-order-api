using OrderApi.Domain.Common;

namespace OrderApi.Domain.Entities;

public class Order : IAuditable, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;

    /// <summary>Optimistic concurrency token (Postgres xmin).</summary>
    public uint Version { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    public decimal Total => Items.Sum(item => item.Subtotal);
    public bool IsEditable => Status == OrderStatus.Open;
    public bool IsCanceled => Status == OrderStatus.Canceled;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
