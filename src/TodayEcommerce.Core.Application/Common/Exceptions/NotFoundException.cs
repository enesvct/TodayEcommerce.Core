namespace TodayEcommerce.Core.Application.Common.Exceptions;

/// <summary>
/// A referenced resource does not exist. Translated to HTTP 404 by the API layer.
/// Lives in Application (not Domain) because "not found" is a use-case concern, not a business rule.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} '{key}' was not found.")
    {
    }
}
