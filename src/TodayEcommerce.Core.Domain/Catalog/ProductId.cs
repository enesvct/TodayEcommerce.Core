namespace TodayEcommerce.Core.Domain.Catalog;

/// <summary>
/// Strongly typed identifier: prevents passing an OrderId where a ProductId is expected.
/// </summary>
public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
