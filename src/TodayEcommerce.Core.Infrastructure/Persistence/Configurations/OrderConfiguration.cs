using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new OrderId(value))
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .HasConversion(id => id.Value, value => new CustomerId(value));

        // "My orders, newest first": the index both finds the customer's rows and already holds them sorted,
        // so the page is read straight from the index instead of sorting every order of the customer.
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAtUtc }).IsDescending(false, true);

        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength();

        // Stored as text ("Placed") instead of a number: readable in SQL and safe if enum values get reordered.
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(o => o.CreatedAtUtc);
        builder.Property(o => o.PlacedAtUtc);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // Calculated from the lines on every read; not stored.
        builder.Ignore(o => o.Total);

        // Order items belong to the Order aggregate: no DbSet of their own, loaded and saved with the order.
        builder.OwnsMany(o => o.Items, items =>
        {
            items.ToTable("order_items");
            items.WithOwner().HasForeignKey("order_id");

            items.HasKey(i => i.Id);
            items.Property(i => i.Id)
                .HasConversion(id => id.Value, value => new OrderItemId(value))
                .ValueGeneratedNever();

            // Reference to another aggregate by id only — deliberately no foreign key to products,
            // so the Catalog and Ordering modules stay independent.
            items.Property(i => i.ProductId)
                .HasConversion(id => id.Value, value => new ProductId(value));

            items.Property(i => i.ProductName).HasMaxLength(Product.NameMaxLength);

            items.OwnsOne(i => i.UnitPrice, price =>
            {
                price.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(18, 2);
                price.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3).IsFixedLength();
            });
            items.Navigation(i => i.UnitPrice).IsRequired();

            items.Property(i => i.Quantity)
                .HasConversion(q => q.Value, value => Quantity.Create(value));

            items.Ignore(i => i.LineTotal);
        });

        // EF writes to the private _items list (the public Items property is a read-only view).
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        ConcurrencyToken.Configure(builder);
    }
}
