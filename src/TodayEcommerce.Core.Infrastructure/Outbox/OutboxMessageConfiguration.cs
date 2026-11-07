using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TodayEcommerce.Core.Infrastructure.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(500);
        builder.Property(m => m.Content).HasColumnType("jsonb");
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // The processor only ever looks for pending messages: a partial index keeps that lookup fast
        // no matter how many millions of processed rows accumulate.
        builder.HasIndex(m => m.OccurredOnUtc)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_on_utc IS NULL");
    }
}
