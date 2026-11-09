using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new StockReservationId(value))
            .ValueGeneratedNever();

        // One reservation per order — also the last line of defence against reserving twice.
        // Deliberately no foreign key to orders: that table belongs to another module.
        builder.HasIndex(r => r.OrderId).IsUnique();

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(r => r.IsActive);

        builder.OwnsMany(r => r.Items, items =>
        {
            items.ToTable("stock_reservation_items");
            items.WithOwner().HasForeignKey("stock_reservation_id");
            items.Property<int>("id").ValueGeneratedOnAdd();
            items.HasKey("id");

            items.Property(i => i.ProductId).HasConversion(id => id.Value, value => new ProductId(value));
        });
        builder.Navigation(r => r.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        ConcurrencyToken.Configure(builder);
    }
}
