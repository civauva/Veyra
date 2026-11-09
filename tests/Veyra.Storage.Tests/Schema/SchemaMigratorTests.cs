using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Schema;
using Veyra.Storage.Schema;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Schema;

[Collection(PostgresGroup.Name)]
public class SchemaMigratorTests(PostgresFixture postgres)
{
    private static readonly SchemaMigration _createTable =
        new("sample", 1, "create_item", "CREATE TABLE sample.item (id int);");
    private static readonly SchemaMigration _addColumn =
        new("sample", 2, "add_label", "ALTER TABLE sample.item ADD label text;");

    [Fact]
    public async Task MigrateAsync_EmptyDatabase_AppliesScriptsInVersionOrder()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var migrator = CreateMigrator(database, _addColumn, _createTable);

        var applied = await migrator.MigrateAsync(CancellationToken.None);

        Assert.Equal(2, applied);
        Assert.Equal(["id", "label"], await ColumnsAsync(database, "sample", "item"));
    }

    [Fact]
    public async Task MigrateAsync_RunTwice_AppliesEachScriptOnce()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await CreateMigrator(database, _createTable, _addColumn).MigrateAsync(CancellationToken.None);

        var appliedAgain = await CreateMigrator(database, _createTable, _addColumn)
            .MigrateAsync(CancellationToken.None);

        Assert.Equal(0, appliedAgain);
        Assert.Equal(2L, await ScalarAsync<long>(database, "SELECT count(*) FROM storage.schema_migration"));
    }

    [Fact]
    public async Task MigrateAsync_FailingScript_RollsBackAllPendingScripts()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var broken = new SchemaMigration("sample", 2, "broken", "ALTER TABLE sample.missing ADD label text;");

        await Assert.ThrowsAsync<PostgresException>(
            () => CreateMigrator(database, _createTable, broken).MigrateAsync(CancellationToken.None));

        Assert.False(await ScalarAsync<bool>(database, "SELECT to_regclass('sample.item') IS NOT NULL"));
        Assert.False(await ScalarAsync<bool>(database, "SELECT to_regclass('storage.schema_migration') IS NOT NULL"));
    }

    [Fact]
    public async Task MigrateAsync_AppliedScriptChanged_ThrowsInvalidOperationException()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        await CreateMigrator(database, _createTable).MigrateAsync(CancellationToken.None);
        var edited = _createTable with { Sql = "CREATE TABLE sample.item (id bigint);" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateMigrator(database, edited).MigrateAsync(CancellationToken.None));

        Assert.Contains("sample", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MigrateAsync_ConcurrentRuns_ApplyEachScriptOnce()
    {
        await using var database = await postgres.CreateDatabaseAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(
            _ => CreateMigrator(database, _createTable, _addColumn).MigrateAsync(CancellationToken.None)));

        Assert.Equal(2, results.Sum());
        Assert.Equal(2L, await ScalarAsync<long>(database, "SELECT count(*) FROM storage.schema_migration"));
    }

    [Fact]
    public async Task MigrateAsync_LineEndingsDiffer_TreatsScriptAsUnchanged()
    {
        await using var database = await postgres.CreateDatabaseAsync();
        var unix = new SchemaMigration("sample", 1, "create_item", "CREATE TABLE sample.item\n(id int);");
        var windows = unix with { Sql = "CREATE TABLE sample.item\r\n(id int);" };
        await CreateMigrator(database, unix).MigrateAsync(CancellationToken.None);

        var applied = await CreateMigrator(database, windows).MigrateAsync(CancellationToken.None);

        Assert.Equal(0, applied);
    }

    private static SchemaMigrator CreateMigrator(NpgsqlDataSource database, params SchemaMigration[] migrations) =>
        new(database, [new FixedSource(migrations)], TimeProvider.System, NullLogger<SchemaMigrator>.Instance);

    private static async Task<T> ScalarAsync<T>(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> ColumnsAsync(NpgsqlDataSource database, string schema, string table)
    {
        await using var command = database.CreateCommand(
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = $1 AND table_name = $2
            ORDER BY ordinal_position
            """);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(table);

        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private sealed class FixedSource(IReadOnlyList<SchemaMigration> migrations) : ISchemaMigrationSource
    {
        public IReadOnlyList<SchemaMigration> GetMigrations() => migrations;
    }
}
