using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Logs;

[Collection(PostgresGroup.Name)]
public class LogRetentionTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_PartitionsOlderThanRetention_AreDroppedWithTheirRecords()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now.AddDays(-20), CancellationToken.None);
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync([Record(_now.AddDays(-20), "old"), Record(_now, "new")], default);

        var result = await Retention(database, new StorageOptions { LogRetentionDays = 14 }).ApplyAsync(_now, default);

        Assert.Contains("log_record_20261020", result.DroppedPartitions);
        Assert.DoesNotContain("log_record_20261109", result.DroppedPartitions);
        Assert.Equal(["new"], await BodiesAsync(database));
    }

    [Fact]
    public async Task ApplyAsync_OldRowsInTheDefaultPartition_AreDeletedButFutureRowsKept()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync(
            [Record(_now.AddDays(-60), "ancient"), Record(_now.AddDays(30), "future")],
            default);

        var result = await Retention(database, new StorageOptions { LogRetentionDays = 14 }).ApplyAsync(_now, default);

        Assert.Equal(1, result.DeletedDefaultRows);
        Assert.Equal(["future"], await BodiesAsync(database));
    }

    [Fact]
    public async Task ApplyAsync_OverTheSizeBudget_DropsTheOldestPartitionsButKeepsToday()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now.AddDays(-3), CancellationToken.None);
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync(
            [Record(_now.AddDays(-3), "three days ago"), Record(_now.AddDays(-1), "yesterday"), Record(_now, "today")],
            default);

        // A budget of about one kilobyte: far too small, so everything before today has to go.
        var tiny = new StorageOptions { LogRetentionDays = 14, MaxLogStorageGigabytes = 0.000001 };
        var result = await Retention(database, tiny).ApplyAsync(_now, default);

        Assert.Equal("log_record_20261105", result.DroppedPartitions[0]);
        Assert.Equal(["today"], await BodiesAsync(database));
    }

    [Fact]
    public async Task ApplyAsync_WithinRetentionAndBudget_DropsNothing()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync([Record(_now.AddDays(-1), "yesterday")], default);

        var result = await Retention(database, new StorageOptions { LogRetentionDays = 14 }).ApplyAsync(_now, default);

        Assert.Empty(result.DroppedPartitions);
        Assert.Equal(0, result.DeletedDefaultRows);
    }

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private static LogPartitions PartitionsAsync(NpgsqlDataSource database) =>
        new(database, TimeProvider.System, NullLogger<LogPartitions>.Instance);

    private static LogRetention Retention(NpgsqlDataSource database, StorageOptions options) =>
        new(database, Options.Create(options), TimeProvider.System, NullLogger<LogRetention>.Instance);

    private static LogRecord Record(DateTimeOffset timestamp, string body) => new(
        timestamp,
        timestamp,
        LogSeverity.Information,
        null,
        body,
        "payments",
        null,
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());

    private static async Task<List<string>> BodiesAsync(NpgsqlDataSource database)
    {
        await using var command = database.CreateCommand("SELECT body FROM storage.log_record ORDER BY timestamp");
        await using var reader = await command.ExecuteReaderAsync();

        var bodies = new List<string>();

        while (await reader.ReadAsync())
        {
            bodies.Add(reader.GetString(0));
        }

        return bodies;
    }
}
