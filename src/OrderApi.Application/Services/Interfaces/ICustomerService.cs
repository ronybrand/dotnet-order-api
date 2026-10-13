using OrderApi.Domain.Common.Search;
using OrderApi.Application.DTOs;

namespace OrderApi.Application.Services.Interfaces;

public interface ICustomerService
{
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);
    Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<CustomerDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
}
