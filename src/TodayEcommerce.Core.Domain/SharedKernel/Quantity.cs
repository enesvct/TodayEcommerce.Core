using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.SharedKernel;

/// <summary>
/// A requested number of units (e.g. an order line quantity). Always at least 1.
/// </summary>
/// <remarks>
/// Deliberately different from stock level: stock may legitimately be 0,
/// but asking for 0 or negative units is meaningless.
/// </remarks>
public sealed record Quantity
{
    public int Value { get; }

    private Quantity(int value)
    {
        Value = value;
    }

    public static Quantity Create(int value)
    {
        if (value < 1)
        {
            throw new DomainException($"Quantity must be at least 1. Given: {value}");
        }

        return new Quantity(value);
    }

    public Quantity Add(Quantity other) => new(checked(Value + other.Value));

    public override string ToString() => Value.ToString();
}
