using TodayEcommerce.Core.Domain.Common;

namespace TodayEcommerce.Core.Domain.SharedKernel;

/// <summary>
/// An amount of money in a specific currency. Immutable and always valid:
/// it cannot be negative and cannot be combined with a different currency.
/// </summary>
/// <remarks>
/// Implemented as a <c>record</c> so equality is value-based (two Money instances with the same
/// amount and currency are equal). Properties are get-only, so the <c>with</c> expression cannot
/// be used to bypass the validation in <see cref="Create"/>.
/// </remarks>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException($"Money amount cannot be negative. Given: {amount}");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new DomainException($"Currency must be a 3-letter ISO code (e.g. TRY, USD). Given: '{currency}'");
        }

        // Normalize so "try" and "TRY" are treated as the same currency.
        return new Money(decimal.Round(amount, 2, MidpointRounding.AwayFromZero), currency.Trim().ToUpperInvariant());
    }

    public static Money Zero(string currency) => Create(0, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int factor)
    {
        if (factor < 0)
        {
            throw new DomainException($"Money cannot be multiplied by a negative factor. Given: {factor}");
        }

        return new Money(Amount * factor, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new DomainException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
