using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Common.Behaviors;
using TodayEcommerce.Core.Application.Common.Events;

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
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(AssemblyReference.Assembly);

            // Order of AddOpenBehavior calls = order in the pipeline:
            // validate first (an invalid request must never hit the cache), then try the cache.
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(CachingBehavior<,>));
        });

        // Finds every AbstractValidator<T> in this assembly (internal ones included).
        services.AddValidatorsFromAssembly(AssemblyReference.Assembly, includeInternalTypes: true);

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        AddDomainEventHandlers(services);

        return services;
    }

    /// <summary>
    /// Registers each class once per IDomainEventHandler&lt;TEvent&gt; it implements,
    /// so a new handler is picked up without touching DI code (like MediatR's assembly scan).
    /// </summary>
    private static void AddDomainEventHandlers(IServiceCollection services)
    {
        var registrations =
            from type in AssemblyReference.Assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false }
            from contract in type.GetInterfaces()
            where contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)
            select (contract, type);

        foreach (var (contract, implementation) in registrations)
        {
            services.AddScoped(contract, implementation);
        }
    }
}
