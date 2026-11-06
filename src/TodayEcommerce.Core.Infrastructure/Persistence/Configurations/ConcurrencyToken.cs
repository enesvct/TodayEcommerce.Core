using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Configurations;

/// <summary>
/// Optimistic concurrency without touching the domain model.
/// PostgreSQL keeps a hidden system column "xmin" on every row that changes on each update —
/// effectively a free version number. Npgsql maps a uint row-version property onto it.
/// EF adds "WHERE xmin = @original" to every UPDATE/DELETE; if another request changed the row
/// in between, 0 rows match and EF throws DbUpdateConcurrencyException (the Ayşe/Mehmet scenario).
/// </summary>
internal static class ConcurrencyToken
{
    public const string PropertyName = "Version";

    public static void Configure<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        // Shadow property: exists only in the EF model, not as a C# property on the entity.
        builder.Property<uint>(PropertyName).IsRowVersion();
    }
}
