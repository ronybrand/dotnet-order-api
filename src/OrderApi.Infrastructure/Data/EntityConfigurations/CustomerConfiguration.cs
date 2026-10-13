using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderApi.Domain.Entities;

namespace OrderApi.Infrastructure.Data.EntityConfigurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public const string TaxIdIndex = "uq_customers_tax_id";
    public const string PassportIndex = "uq_customers_passport_number";

    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.TaxId).IsRequired().HasMaxLength(20);
        builder.Property(c => c.PassportNumber).HasMaxLength(9);
        builder.Property(c => c.Email).IsRequired().HasMaxLength(320);
        builder.Property(c => c.CreatedBy).IsRequired().HasMaxLength(200);
        builder.Property(c => c.UpdatedBy).IsRequired().HasMaxLength(200);
        builder.Property(c => c.DeletedBy).HasMaxLength(200);

        // Unique among active customers only: a soft-deleted customer releases its tax id and
        // passport number (the service-level checks only consider active records as well).
        builder.HasIndex(c => c.TaxId).IsUnique().HasDatabaseName(TaxIdIndex)
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(c => c.PassportNumber).IsUnique().HasDatabaseName(PassportIndex)
            .HasFilter("deleted_at IS NULL AND passport_number IS NOT NULL");
    }
}
