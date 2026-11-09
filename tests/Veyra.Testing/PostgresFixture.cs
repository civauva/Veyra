using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Veyra.Core.Schema;
using Veyra.Storage.Schema;

namespace Veyra.Testing;

/// <summary>
/// Starts one throwaway PostgreSQL container for all tests in a collection.
/// Each test gets its own empty database, so tests never see each other's tables.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // The same major version that deploy/docker-compose.yml runs in production.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();

    private int _databaseCount;

    /// <summary>Starts the container. xUnit calls it once before the first test of the collection.</summary>
    /// <returns>A task that completes when PostgreSQL accepts connections.</returns>
    public Task InitializeAsync() => _container.StartAsync();

    /// <summary>Stops and removes the container after the last test of the collection.</summary>
    /// <returns>A task that completes when the container is gone.</returns>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Creates a new, empty database and returns a data source for it.
    /// </summary>
    /// <returns>A data source for the new database. The caller disposes it.</returns>
    public async Task<NpgsqlDataSource> CreateDatabaseAsync() =>
        NpgsqlDataSource.Create(await CreateDatabaseConnectionStringAsync());

    /// <summary>
    /// Creates a new database and applies the given modules' migrations with the real migrator.
    /// </summary>
    /// <param name="sources">The migration sources of the modules under test.</param>
    /// <returns>A data source for the migrated database. The caller disposes it.</returns>
    public async Task<NpgsqlDataSource> CreateMigratedDatabaseAsync(params ISchemaMigrationSource[] sources)
    {
        var database = await CreateDatabaseAsync();
        var migrator = new SchemaMigrator(database, sources, TimeProvider.System, NullLogger<SchemaMigrator>.Instance);

        await migrator.MigrateAsync(CancellationToken.None);

        return database;
    }

    /// <summary>
    /// Returns the connection string of a new, empty database, for tests that start the whole application.
    /// </summary>
    /// <returns>A connection string that points at the new database.</returns>
    public async Task<string> CreateDatabaseConnectionStringAsync()
    {
        var name = $"test_{Interlocked.Increment(ref _databaseCount)}";

        await using (var admin = NpgsqlDataSource.Create(_container.GetConnectionString()))
        {
            await using var create = admin.CreateCommand($"CREATE DATABASE {name}");
            await create.ExecuteNonQueryAsync();
        }

        // Built from the container settings, because NpgsqlDataSource.ConnectionString leaves out the password.
        return ConnectionStringFor(name);
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
}
