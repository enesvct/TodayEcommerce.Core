using TodayEcommerce.Core.Domain.Catalog.Events;
using TodayEcommerce.Core.Domain.Common;
using TodayEcommerce.Core.Domain.SharedKernel;

namespace TodayEcommerce.Core.Domain.Catalog;

/// <summary>
/// Catalog product. Aggregate root: name, price and stock level can only change
/// through intention-revealing methods that enforce the business rules.
/// </summary>
public sealed class Product : AggregateRoot<ProductId>
{
    public const int NameMaxLength = 200;

    public string Name { get; private set; }
    public Money Price { get; private set; }

    /// <summary>
    /// Units on hand. May be 0 (sold out) but never negative.
    /// Kept as int (not <see cref="Quantity"/>) because 0 is a valid stock level.
    /// </summary>
    public int StockQuantity { get; private set; }

    /// <summary>
    /// For EF Core materialization only. Private, so application code cannot create an invalid Product.
    /// </summary>
    private Product()
    {
        Name = null!;
        Price = null!;
    }

    private Product(ProductId id, string name, Money price, int stockQuantity)
        : base(id)
    {
        Name = name;
        Price = price;
        StockQuantity = stockQuantity;
    }

    public static Product Create(string name, Money price, int initialStock)
    {
        if (initialStock < 0)
        {
            throw new DomainException($"Initial stock cannot be negative. Given: {initialStock}");
        }

        var product = new Product(ProductId.New(), ValidateName(name), price, initialStock);
        product.Raise(new ProductCreatedDomainEvent(product.Id));
        return product;
    }

    public void Rename(string newName)
    {
        var validated = ValidateName(newName);
        if (validated == Name)
        {
            return; // nothing happened → no event
        }

        var oldName = Name;
        Name = validated;
        Raise(new ProductRenamedDomainEvent(Id, oldName, validated));
    }

    public void ChangePrice(Money newPrice)
    {
        if (newPrice.Currency != Price.Currency)
        {
            // Switching currency is a different business decision (repricing a market), not a price update.
            throw new DomainException($"Price currency cannot change from {Price.Currency} to {newPrice.Currency}.");
        }

        if (newPrice == Price)
        {
            return; // same value → no change, no event (value-object equality at work)
        }

        var oldPrice = Price;
        Price = newPrice;
        Raise(new ProductPriceChangedDomainEvent(Id, oldPrice.Amount, newPrice.Amount, newPrice.Currency));
    }

    public void AddStock(Quantity quantity)
    {
        StockQuantity = checked(StockQuantity + quantity.Value);
        Raise(new ProductStockChangedDomainEvent(Id, StockQuantity));
    }

    public void RemoveStock(Quantity quantity)
    {
        EnsureStockFor(quantity);

        StockQuantity -= quantity.Value;
        Raise(new ProductStockChangedDomainEvent(Id, StockQuantity));
    }

    public bool HasStockFor(Quantity quantity) => StockQuantity >= quantity.Value;

    /// <summary>The "insufficient stock" rule and its message live here — not in application handlers.</summary>
    public void EnsureStockFor(Quantity quantity)
    {
        if (!HasStockFor(quantity))
        {
            throw new DomainException(
                $"Insufficient stock for product '{Name}'. Requested: {quantity.Value}, available: {StockQuantity}");
        }
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new DomainException($"Product name cannot exceed {NameMaxLength} characters.");
        }

        return trimmed;
    }
}
