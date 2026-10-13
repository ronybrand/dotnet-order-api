using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderApi.Domain.Entities;

namespace OrderApi.Infrastructure.Data.EntityConfigurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.Description).IsRequired().HasMaxLength(255);
        builder.Property(i => i.UnitPrice).HasColumnType("numeric(12,2)");
        builder.HasIndex(i => i.OrderId);
    }
}
