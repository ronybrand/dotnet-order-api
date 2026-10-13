using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;

namespace OrderApi.Domain.Interfaces;

public interface IOrderRepository
{
    /// <summary>Returns the active (non soft-deleted) order with its items, or null.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Persists changes of a tracked order (including items). Throws on version conflict.</summary>
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<PagedResult<Order>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
