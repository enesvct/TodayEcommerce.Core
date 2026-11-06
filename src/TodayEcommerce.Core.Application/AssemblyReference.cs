using System.Reflection;

namespace TodayEcommerce.Core.Application;

/// <summary>
/// Marker type used to locate this assembly (e.g. in architecture tests and DI scanning).
/// </summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
