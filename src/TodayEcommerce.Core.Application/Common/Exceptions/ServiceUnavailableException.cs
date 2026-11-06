namespace TodayEcommerce.Core.Application.Common.Exceptions;

/// <summary>
/// A dependency the request cannot work without is down (e.g. the search engine).
/// Translated to HTTP 503 so clients know to retry later — it is neither their fault (4xx) nor a bug (500).
/// </summary>
public sealed class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
