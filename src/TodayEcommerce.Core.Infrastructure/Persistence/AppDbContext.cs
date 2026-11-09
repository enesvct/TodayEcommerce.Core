using Microsoft.EntityFrameworkCore;
using Npgsql;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Common.Exceptions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Infrastructure.Idempotency;
using TodayEcommerce.Core.Infrastructure.Outbox;

namespace TodayEcommerce.Core.Infrastructure.Persistence;

/// <summary>
/// EF Core session. Also the Unit of Work: SaveChangesAsync commits every tracked change in one transaction —
/// including the outbox messages for the domain events raised by those changes.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    internal DbSet<IdempotencyRecordEntity> IdempotencyRecords => Set<IdempotencyRecordEntity>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 1. Turn pending domain events into outbox rows. They are added to THIS unit of work,
        //    so the aggregate change and its events commit (or roll back) together — no dual write.
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();

        var now = DateTime.UtcNow;
        OutboxMessages.AddRange(aggregates
            .SelectMany(a => a.DomainEvents)
            .Select(e => OutboxSerializer.ToMessage(e, now)));

        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);

            // 2. Only after a successful commit: the events are safely stored, don't write them twice.
            aggregates.ForEach(a => a.ClearDomainEvents());
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Translate the EF-specific exception at the boundary so outer layers never see EF types.
            var entity = ex.Entries.FirstOrDefault()?.Metadata.ClrType.Name ?? "Entity";
            throw new ConcurrencyException(
                $"{entity} was modified by another request after it was read. Reload and try again.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: IdempotencyRecordConfiguration.TableName,
        })
        {
            throw new DuplicateIdempotencyKeyException(ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // All mapping lives in IEntityTypeConfiguration classes in this assembly — never as attributes in Domain.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Pending domain events are in-memory only; they are persisted via the outbox, not as columns.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IHasDomainEvents).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType).Ignore(nameof(IHasDomainEvents.DomainEvents));
        }
    }
}
