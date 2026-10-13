using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderApi.Domain.Common.Search;
using OrderApi.Domain.Entities;
using OrderApi.Domain.Exceptions;
using OrderApi.Domain.Interfaces;
using OrderApi.Infrastructure.Data;
using OrderApi.Infrastructure.Data.EntityConfigurations;
using OrderApi.Infrastructure.Search;

namespace OrderApi.Infrastructure.Repositories;

public sealed class CustomerRepository(AppDbContext db) : ICustomerRepository
{
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsByTaxIdAsync(string taxId, Guid? excludeId, CancellationToken cancellationToken = default) =>
        db.Customers.AnyAsync(c => c.TaxId == taxId && (excludeId == null || c.Id != excludeId), cancellationToken);

    public Task<bool> ExistsByPassportNumberAsync(string passportNumber, Guid? excludeId, CancellationToken cancellationToken = default) =>
        db.Customers.AnyAsync(c => c.PassportNumber == passportNumber && (excludeId == null || c.Id != excludeId), cancellationToken);

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        db.Customers.Add(customer);
        await SaveAsync(cancellationToken);
    }

    public Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default) => SaveAsync(cancellationToken);

    public Task<PagedResult<Customer>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
        db.Customers.AsNoTracking().SearchAsync(request, db.Model.FindEntityType(typeof(Customer))!, cancellationToken);

    // The unique indexes are the real guarantee under concurrency: the service-level check only
    // gives the common case a readable 409 without a failed write.
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw pg.ConstraintName switch
            {
                CustomerConfiguration.TaxIdIndex => new ConflictDomainException(
                    "Customer with this tax_id already exists", ErrorCodes.ConflictTaxId),
                CustomerConfiguration.PassportIndex => new ConflictDomainException(
                    "Customer with this passport_number already exists", ErrorCodes.ConflictPassportNumber),
                _ => ex,
            };
        }
    }
}
