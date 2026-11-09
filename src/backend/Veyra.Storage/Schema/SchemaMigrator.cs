using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Core.Schema;

namespace Veyra.Storage.Schema;

/// <summary>
/// Applies every module's pending schema migrations to the PostgreSQL database.
/// </summary>
/// <remarks>
/// <para>
/// All pending scripts run in one transaction. If any script fails, nothing is applied and Veyra does not start,
/// so the database is never left half-migrated.
/// </para>
/// <para>
/// A PostgreSQL advisory lock serialises concurrent runs. When two Veyra instances start at the same time, the second
/// waits for the first and then finds nothing left to do.
/// </para>
/// <para>
/// Applied scripts are recorded in <c>storage.schema_migration</c> with a checksum. Migrations only move forward:
/// if a script that was already applied has changed, the run stops with an error instead of guessing.
/// </para>
/// </remarks>
internal sealed partial class SchemaMigrator(
    NpgsqlDataSource dataSource,
    IEnumerable<ISchemaMigrationSource> sources,
    TimeProvider clock,
    ILogger<SchemaMigrator> logger)
{
    // Any constant works. It only has to be the same for every Veyra instance that uses this database.
    // The value is "Veyra" in ASCII, which makes the lock easy to recognise in pg_locks.
    private const long _advisoryLockKey = 0x5665797261;

    private const string _createHistorySql = """
        CREATE SCHEMA IF NOT EXISTS storage;

        CREATE TABLE IF NOT EXISTS storage.schema_migration (
            module      text        NOT NULL,
            version     integer     NOT NULL,
            name        text        NOT NULL,
            checksum    text        NOT NULL,
            applied_at  timestamptz NOT NULL,
            PRIMARY KEY (module, version)
        );
        """;

    /// <summary>
    /// Applies all migrations that have not been applied yet.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting for the lock and running the scripts.</param>
    /// <returns>The number of scripts applied by this call. Zero when the database was already up to date.</returns>
    /// <exception cref="InvalidOperationException">
    /// A script that was already applied has been changed since.
    /// </exception>
    /// <exception cref="PostgresException">A script failed. Nothing from this run is applied.</exception>
    public async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        var migrations = sources
            .SelectMany(source => source.GetMigrations())
            .OrderBy(migration => migration.Module, StringComparer.Ordinal)
            .ThenBy(migration => migration.Version)
            .ToList();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The lock is released automatically when the transaction ends, also when the process dies.
        await ExecuteAsync(connection, $"SELECT pg_advisory_xact_lock({_advisoryLockKey})", cancellationToken);
        await ExecuteAsync(connection, _createHistorySql, cancellationToken);

        var applied = await LoadAppliedAsync(connection, cancellationToken);
        var count = 0;

        foreach (var migration in migrations)
        {
            var checksum = Checksum(migration.Sql);

            if (applied.TryGetValue((migration.Module, migration.Version), out var appliedChecksum))
            {
                if (appliedChecksum != checksum)
                {
                    throw new InvalidOperationException(
                        $"Migration {migration.Module} V{migration.Version:D4} \"{migration.Name}\" was changed "
                        + "after it was applied. Migrations only move forward: "
                        + "add a new script instead of editing an old one.");
                }

                continue;
            }

            // Module names are validated identifiers (see SchemaMigrationScripts), so they are safe to inline here.
            await ExecuteAsync(connection, $"CREATE SCHEMA IF NOT EXISTS {migration.Module}", cancellationToken);
            await ExecuteAsync(connection, migration.Sql, cancellationToken);
            await RecordAsync(connection, migration, checksum, cancellationToken);

            LogApplied(migration.Module, migration.Version, migration.Name);
            count++;
        }

        await transaction.CommitAsync(cancellationToken);

        LogCompleted(count, migrations.Count);

        return count;
    }

    /// <summary>
    /// Hashes a script so that later edits can be detected.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised first, so a checkout with Windows line endings does not count as an edit.
    /// </remarks>
    private static string Checksum(string sql)
    {
        var normalised = sql.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<(string Module, int Version), string>> LoadAppliedAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT module, version, checksum FROM storage.schema_migration",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var applied = new Dictionary<(string Module, int Version), string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            applied[(reader.GetString(0), reader.GetInt32(1))] = reader.GetString(2);
        }

        return applied;
    }

    private async Task RecordAsync(
        NpgsqlConnection connection,
        SchemaMigration migration,
        string checksum,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO storage.schema_migration (module, version, name, checksum, applied_at)
            VALUES ($1, $2, $3, $4, $5)
            """,
            connection);

        command.Parameters.AddWithValue(migration.Module);
        command.Parameters.AddWithValue(migration.Version);
        command.Parameters.AddWithValue(migration.Name);
        command.Parameters.AddWithValue(checksum);
        command.Parameters.AddWithValue(clock.GetUtcNow());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied migration {Module} V{Version} {Name}")]
    private partial void LogApplied(string module, int version, string name);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Database schema is up to date: applied {Applied} of {Total} migrations")]
    private partial void LogCompleted(int applied, int total);
}
