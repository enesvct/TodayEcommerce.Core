using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodayEcommerce.Core.Domain.Catalog;

namespace TodayEcommerce.Core.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ProductId(value)) // strongly typed id ⇄ uuid column
            .ValueGeneratedNever();                                      // the domain generates ids, not the database

        builder.Property(p => p.Name)
            .HasMaxLength(Product.NameMaxLength)
            .IsRequired();

        // Value object → columns on the same table (price_amount, price_currency), no separate table.
        builder.OwnsOne(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(18, 2);
            price.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3).IsFixedLength();
        });
        builder.Navigation(p => p.Price).IsRequired();

        builder.Property(p => p.StockQuantity);
        builder.ToTable(t => t.HasCheckConstraint("ck_products_stock_non_negative", "stock_quantity >= 0"));

        ConcurrencyToken.Configure(builder);
    }
}
