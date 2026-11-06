using Microsoft.Extensions.DependencyInjection;

namespace TodayEcommerce.Core.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application services. Called once from the WebApi composition root (Program.cs).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Scans this assembly and registers every IRequestHandler it finds,
        // so adding a new use-case never requires touching DI code.
        services.AddMediatR(config => config.RegisterServicesFromAssembly(AssemblyReference.Assembly));

        return services;
    }
}
