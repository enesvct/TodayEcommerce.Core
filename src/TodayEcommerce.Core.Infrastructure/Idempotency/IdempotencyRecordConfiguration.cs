using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TodayEcommerce.Core.Infrastructure.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecordEntity>
{
    public const string TableName = "idempotency_records";

    public void Configure(EntityTypeBuilder<IdempotencyRecordEntity> builder)
    {
        builder.ToTable(TableName);

        // Keys are scoped per user: Mehmet cannot see (or block) Ayşe's request by guessing her key.
        // The primary key is also the guard against two concurrent requests with the same key.
        builder.HasKey(r => new { r.UserId, r.Key });

        builder.Property(r => r.Key).HasMaxLength(100);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength();

        // For the cleanup job that deletes expired records.
        builder.HasIndex(r => r.CreatedOnUtc);
    }
}
