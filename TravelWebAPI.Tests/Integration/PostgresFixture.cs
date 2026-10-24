using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Integration;

/// <summary>
/// One throw-away PostgreSQL container shared by all integration tests. Each test class asks for its
/// own empty database so tests never see each other's data (and never touch the developer database).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public async Task<string> CreateDatabaseAsync(bool migrate = true)
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;

        if (migrate)
        {
            await using var context = CreateContext(connectionString);
            await context.Database.MigrateAsync();
        }

        return connectionString;
    }

    public static AppDbContext CreateContext(string connectionString, TimeProvider? time = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseDatabase(connectionString).Options;
        return new AppDbContext(options, new SystemClock(time ?? TimeProvider.System));
    }

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
