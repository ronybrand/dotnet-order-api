using OrderApi.Application.Common;
using OrderApi.Application.DTOs;
using OrderApi.Application.Services.Interfaces;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Domain.Interfaces;

namespace OrderApi.Application.Services;

public sealed class CustomerService(
    ICustomerRepository customers,
    IOrderRepository orders,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ICustomerService
{
    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var taxId = CanonicalTaxId(request.TaxId);
        await AssertUniqueAsync(taxId, request.PassportNumber, null, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var customer = new Customer
        {
            Name = request.Name,
            TaxId = taxId,
            PassportNumber = NormalizePassport(request.PassportNumber),
            Email = request.Email,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = currentUser.UserName,
            UpdatedBy = currentUser.UserName,
        };
        await customers.AddAsync(customer, cancellationToken);
        return MapToDto(customer);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        MapToDto(await GetActiveAsync(id, cancellationToken));

    public async Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        var customer = await GetActiveAsync(id, cancellationToken);
        var taxId = CanonicalTaxId(request.TaxId);
        await AssertUniqueAsync(taxId, request.PassportNumber, id, cancellationToken);

        customer.Name = request.Name;
        customer.TaxId = taxId;
        customer.PassportNumber = NormalizePassport(request.PassportNumber);
        customer.Email = request.Email;
        customer.UpdatedAt = timeProvider.GetUtcNow();
        customer.UpdatedBy = currentUser.UserName;
        await customers.UpdateAsync(customer, cancellationToken);
        return MapToDto(customer);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await GetActiveAsync(id, cancellationToken);
        if (await orders.ExistsActiveByCustomerAsync(id, cancellationToken))
        {
            throw new ConflictDomainException(
                $"Customer {id} has orders and cannot be deleted",
                ErrorCodes.ConflictCustomerHasOrders,
                new Dictionary<string, object?> { ["id"] = id });
        }

        customer.DeletedAt = timeProvider.GetUtcNow();
        customer.DeletedBy = currentUser.UserName;
        await customers.UpdateAsync(customer, cancellationToken);
    }

    public async Task<PagedResult<CustomerDto>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        (await customers.SearchAsync(request, cancellationToken)).Map(MapToDto);

    private async Task<Customer> GetActiveAsync(Guid id, CancellationToken cancellationToken) =>
        await customers.GetByIdAsync(id, cancellationToken)
        ?? throw new NotFoundDomainException(
            $"Customer {id} not found",
            ErrorCodes.CustomerNotFound,
            new Dictionary<string, object?> { ["id"] = id });

    private async Task AssertUniqueAsync(string taxId, string? passportNumber, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (await customers.ExistsByTaxIdAsync(taxId, excludeId, cancellationToken))
        {
            // The tax id itself is PII: it is intentionally not echoed in the message or params.
            throw new ConflictDomainException("Customer with this tax_id already exists", ErrorCodes.ConflictTaxId);
        }

        var passport = NormalizePassport(passportNumber);
        if (passport is not null && await customers.ExistsByPassportNumberAsync(passport, excludeId, cancellationToken))
        {
            throw new ConflictDomainException(
                "Customer with this passport_number already exists",
                ErrorCodes.ConflictPassportNumber);
        }
    }

    // Punctuation and case are formatting, not identity: "123.456-78" and "12345678" are the same document,
    // so the stored value (and therefore the unique index) uses a single canonical form.
    private static string CanonicalTaxId(string taxId) =>
        string.Concat(taxId.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();

    private static string? NormalizePassport(string? passportNumber) =>
        string.IsNullOrWhiteSpace(passportNumber) ? null : passportNumber;

    private static CustomerDto MapToDto(Customer customer) => new(customer.Id, customer.Name);
}
