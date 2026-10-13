using OrderApi.Domain.Common.Search;
using OrderApi.Application.DTOs;

namespace OrderApi.Application.Services.Interfaces;

public interface IOrderService
{
    Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto> AddItemAsync(Guid orderId, ItemRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto> UpdateItemQuantityAsync(Guid orderId, Guid itemId, int quantity, CancellationToken cancellationToken = default);
    Task<OrderDto> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken cancellationToken = default);
    Task<OrderDto> ConfirmAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OrderDto> CancelAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<OrderDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
