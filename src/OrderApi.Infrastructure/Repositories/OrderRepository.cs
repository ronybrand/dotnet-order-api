using Microsoft.EntityFrameworkCore;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Interfaces;
using OrderApi.Infrastructure.Data;
using OrderApi.Infrastructure.Search;

namespace OrderApi.Infrastructure.Repositories;

public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<bool> ExistsActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        db.Orders.AnyAsync(o => o.CustomerId == customerId, cancellationToken);

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
    }

    // DbUpdateConcurrencyException (stale xmin) is deliberately not caught here: the global
    // exception handler maps it to 409 CONFLICT-00.
    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public Task<PagedResult<Order>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        db.Orders.AsNoTracking().Include(o => o.Items)
            .SearchAsync(request, db.Model.FindEntityType(typeof(Order))!, cancellationToken);
}
