using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using TodayEcommerce.Core.Application.Abstractions;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>
/// The whole API in memory (real middleware, auth, controllers, MediatR, EF Core) against a throw-away
/// PostgreSQL. Every setting the app needs is given here, so the tests never depend on a developer's
/// git-ignored appsettings.Development.json — they behave the same on any machine and in CI.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _environment;

    public ApiFactory(string connectionString, string environment)
    {
        _connectionString = connectionString;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.UseSetting("ConnectionStrings:Database", _connectionString);
        builder.UseSetting("ConnectionStrings:Redis", "localhost:1");
        builder.UseSetting("ConnectionStrings:Elasticsearch", "http://localhost:1");
        builder.UseSetting("Jwt:SigningKey", TestTokens.SigningKey);

        builder.ConfigureTestServices(services =>
        {
            // No background work in these tests (outbox job, search index initializer):
            // they test the HTTP surface, and background timing would only make them flaky.
            services.RemoveAll<IHostedService>();
            services.AddSingleton<ICacheService, NoOpCache>();
        });
    }

    private sealed class NoOpCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class => Task.FromResult<T?>(null);
        public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

/// <summary>One PostgreSQL container (with the real migrations applied) shared by all API tests.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}
