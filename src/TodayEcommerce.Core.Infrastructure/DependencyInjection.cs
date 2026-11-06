using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Infrastructure.Persistence;
using TodayEcommerce.Core.Infrastructure.Persistence.Repositories;
using TodayEcommerce.Core.Infrastructure.Services;

namespace TodayEcommerce.Core.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires the interfaces declared in Domain/Application to their Infrastructure implementations.
    /// Called once from the WebApi composition root (Program.cs).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()); // StockQuantity → stock_quantity (PostgreSQL convention)

        // One AppDbContext per HTTP request (scoped): repositories and the unit of work share it,
        // so everything a handler changes is committed together.
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}
