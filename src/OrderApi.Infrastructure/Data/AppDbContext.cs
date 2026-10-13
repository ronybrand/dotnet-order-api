using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OrderApi.Domain.Common;
using OrderApi.Domain.Entities;

namespace OrderApi.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly MethodInfo SetSoftDeleteFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Reflection is required because HasQueryFilter<T> needs the closed type at compile time.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                SetSoftDeleteFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(null, [modelBuilder]);
            }
        }
    }

    private static void SetSoftDeleteFilter<T>(ModelBuilder builder)
        where T : class, ISoftDeletable =>
        builder.Entity<T>().HasQueryFilter(entity => entity.DeletedAt == null);
}
