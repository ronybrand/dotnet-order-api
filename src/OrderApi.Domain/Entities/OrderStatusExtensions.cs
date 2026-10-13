namespace OrderApi.Domain.Entities;

public static class OrderStatusExtensions
{
    /// <summary>Wire/storage representation: OPEN, CONFIRMED or CANCELED.</summary>
    public static string ToCode(this OrderStatus status) => status.ToString().ToUpperInvariant();
}
