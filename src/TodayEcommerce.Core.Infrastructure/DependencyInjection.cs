using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Application.Ordering.Queries.GetMyOrders;
using TodayEcommerce.Core.Domain.Catalog;
using TodayEcommerce.Core.Domain.Ordering;
using TodayEcommerce.Core.Infrastructure.Caching;
using TodayEcommerce.Core.Infrastructure.HealthChecks;
using TodayEcommerce.Core.Infrastructure.Idempotency;
using TodayEcommerce.Core.Infrastructure.Outbox;
using TodayEcommerce.Core.Infrastructure.Persistence;
using TodayEcommerce.Core.Infrastructure.Persistence.Queries;
using TodayEcommerce.Core.Infrastructure.Persistence.Repositories;
using TodayEcommerce.Core.Infrastructure.Search;
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
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IOrderQueries, OrderQueries>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is not configured.");
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnectionString;
            options.InstanceName = "todayecommerce:"; // prefix so several apps can share one Redis
        });
        services.AddSingleton<ICacheService, RedisCacheService>();

        var elasticsearchUrl = configuration.GetConnectionString("Elasticsearch")
            ?? throw new InvalidOperationException("Connection string 'Elasticsearch' is not configured.");
        services.AddHttpClient(ProductIndex.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(elasticsearchUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(5); // a hung search engine must not hang our requests
        });
        services.AddSingleton<ProductIndex>();
        services.AddSingleton<IProductSearchService, ElasticsearchProductSearchService>();
        services.AddHostedService<ProductSearchIndexInitializer>();

        services.AddSingleton<OutboxProcessor>();
        services.AddTransient<ProcessOutboxJob>();

        // Readiness: "may this instance receive traffic?" Only the database is critical. Redis and
        // Elasticsearch going down degrade the shop (slower, no search) but orders still work — reporting
        // them as Unhealthy would make the platform pull EVERY instance out at once (cascading failure).
        var probeTimeout = TimeSpan.FromSeconds(2); // a hung dependency must not hang the probe
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgresql", HealthStatus.Unhealthy, [HealthCheckTags.Ready], probeTimeout)
            .AddCheck<RedisHealthCheck>("redis", HealthStatus.Degraded, [HealthCheckTags.Ready], probeTimeout)
            .AddCheck<ElasticsearchHealthCheck>("elasticsearch", HealthStatus.Degraded, [HealthCheckTags.Ready], probeTimeout);

        return services;
    }

    /// <summary>
    /// Background job server (Hangfire). Separate from <see cref="AddInfrastructure"/> so tests and tools
    /// can use the persistence layer without starting job workers.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // Same PostgreSQL, own "hangfire" schema: no extra infrastructure to run.
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 2;
            // How often the scheduler checks for due recurring jobs (default 15s, too slow for a 5s schedule).
            options.SchedulePollingInterval = TimeSpan.FromSeconds(1);
        });

        services.AddHostedService<OutboxJobScheduler>();

        return services;
    }
}
