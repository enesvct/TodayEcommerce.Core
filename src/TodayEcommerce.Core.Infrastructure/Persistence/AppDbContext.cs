using Microsoft.EntityFrameworkCore;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;

namespace TodayEcommerce.Core.Infrastructure.Persistence;

/// <summary>
/// EF Core session. Also the Unit of Work: SaveChangesAsync commits every tracked change in one transaction.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // All mapping lives in IEntityTypeConfiguration classes in this assembly — never as attributes in Domain.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
