using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderApi.Domain.Entities;

namespace OrderApi.Infrastructure.Data.EntityConfigurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(o => o.CustomerId).IsRequired();
        builder.Property(o => o.Status)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(status => status.ToCode(), code => Enum.Parse<OrderStatus>(code, true));

        // Optimistic concurrency: Postgres system column xmin changes on every UPDATE of the row.
        builder.Property(o => o.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(o => o.CreatedBy).IsRequired().HasMaxLength(200);
        builder.Property(o => o.UpdatedBy).IsRequired().HasMaxLength(200);
        builder.Property(o => o.DeletedBy).HasMaxLength(200);

        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => o.CustomerId);

        // Item is a child entity of the Order aggregate: its lifecycle is tied to the order.
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
    }
}
