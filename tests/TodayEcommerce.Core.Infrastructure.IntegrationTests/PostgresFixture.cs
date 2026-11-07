using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TodayEcommerce.Core.Infrastructure.Persistence;

namespace TodayEcommerce.Core.Infrastructure.IntegrationTests;

/// <summary>
/// Starts a throw-away PostgreSQL container once for all tests in the collection, applies the real
/// migrations, and removes the container afterwards. No local database or password is needed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Each call = a separate session, like two different HTTP requests.</summary>
    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
