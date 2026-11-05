namespace TodayEcommerce.Core.Domain.Ordering;

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

public readonly record struct OrderItemId(Guid Value)
{
    public static OrderItemId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The customer is owned by another context (identity/customers); Ordering only keeps the reference.
/// </summary>
public readonly record struct CustomerId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
