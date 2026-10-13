namespace OrderApi.Domain.Entities;

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    public decimal Subtotal => UnitPrice * Quantity;
}
