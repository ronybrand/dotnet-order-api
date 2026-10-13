using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;

namespace OrderApi.Domain.Interfaces;

public interface ICustomerRepository
{
    /// <summary>Returns the active (non soft-deleted) customer, or null.</summary>
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByTaxIdAsync(string taxId, Guid? excludeId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPassportNumberAsync(string passportNumber, Guid? excludeId, CancellationToken cancellationToken = default);
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
    Task<PagedResult<Customer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
