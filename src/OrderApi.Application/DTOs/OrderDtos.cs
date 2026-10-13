namespace OrderApi.Application.DTOs;

public sealed record OrderItemDto(Guid Id, Guid OrderId, string Description, decimal UnitPrice, int Quantity, decimal Subtotal);

public sealed record OrderDto(Guid Id, Guid CustomerId, string Status, uint Version, IReadOnlyList<OrderItemDto> Items, decimal Total);

public sealed record ItemRequest(string Description, decimal UnitPrice, int Quantity);

public sealed record CreateOrderRequest(Guid CustomerId, IReadOnlyList<ItemRequest>? Items);

public sealed record UpdateItemQuantityRequest(int Quantity);
