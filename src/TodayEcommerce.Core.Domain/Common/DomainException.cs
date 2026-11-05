namespace TodayEcommerce.Core.Domain.Common;

/// <summary>
/// Thrown when a business rule (invariant) of the domain is violated.
/// Distinguishing it from generic exceptions lets the API translate it into a 400/422 response
/// instead of a 500, because it signals invalid input rather than a system failure.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
