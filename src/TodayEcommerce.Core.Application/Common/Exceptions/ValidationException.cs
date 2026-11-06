namespace TodayEcommerce.Core.Application.Common.Exceptions;

/// <summary>
/// The request is malformed. Carries every failure at once (grouped by property name)
/// so the client can fix all of them in one go. Translated to HTTP 400 by the API layer.
/// </summary>
/// <remarks>
/// Our own type instead of FluentValidation's: the API layer then doesn't need to know
/// which validation library Application uses.
/// </remarks>
public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}
