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

        return new Product(ProductId.New(), ValidateName(name), price, initialStock);
    }

    public void Rename(string newName)
    {
        Name = ValidateName(newName);
    }

    public void ChangePrice(Money newPrice)
    {
        if (newPrice.Currency != Price.Currency)
        {
            // Switching currency is a different business decision (repricing a market), not a price update.
            throw new DomainException($"Price currency cannot change from {Price.Currency} to {newPrice.Currency}.");
        }

        Price = newPrice;
    }

    public void AddStock(Quantity quantity)
    {
        StockQuantity = checked(StockQuantity + quantity.Value);
    }

    public void RemoveStock(Quantity quantity)
    {
        if (!HasStockFor(quantity))
        {
            throw new DomainException(
                $"Insufficient stock for product '{Name}'. Requested: {quantity.Value}, available: {StockQuantity}");
        }

        StockQuantity -= quantity.Value;
    }

    public bool HasStockFor(Quantity quantity) => StockQuantity >= quantity.Value;

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
