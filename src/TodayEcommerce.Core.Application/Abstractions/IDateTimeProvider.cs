namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// Abstracts the system clock so use-cases (and their tests) control "now".
/// The Domain receives the time as a parameter and never reads the clock itself.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
