namespace TodayEcommerce.Core.Application.Common.Exceptions;

/// <summary>
/// The data changed between reading and saving (optimistic concurrency conflict).
/// Infrastructure translates its ORM-specific exception into this one, so neither Application
/// nor the API depends on EF Core. Translated to HTTP 409 by the API layer.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
