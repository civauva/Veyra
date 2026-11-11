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

    [Fact]
    public async Task GetUsageAsync_ReportsSizeBudgetRetentionAndDailyAverage()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now.AddDays(-1), CancellationToken.None);
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync(
            [Record(_now.AddDays(-2), "two days ago"), Record(_now.AddDays(-1), "yesterday"), Record(_now, "today")],
            default);
        var options = new StorageOptions { LogRetentionDays = 7, MaxLogStorageGigabytes = 2 };

        var usage = await Retention(database, options).GetUsageAsync(_now, CancellationToken.None);

        // The full days before today: the 7th and the 8th of November.
        var fullDays = await SizeAsync(database, "storage.log_record_20261107", "storage.log_record_20261108");
        var all = await SizeAsync(
            database,
            "storage.log_record_20261107",
            "storage.log_record_20261108",
            "storage.log_record_20261109",
            "storage.log_record_20261110",
            "storage.log_record_20261111",
            "storage.log_record_default");
        Assert.Equal(all, usage.UsedBytes);
        Assert.Equal(2L * 1024 * 1024 * 1024, usage.BudgetBytes);
        Assert.Equal(7, usage.RetentionDays);
        Assert.Equal(fullDays / 2, usage.DailyBytes);
    }

    [Fact]
    public async Task GetUsageAsync_BeforeTheFirstFullDay_HasNoDailyAverageOrBudget()
    {
        await using var database = await CreateAsync();
        await PartitionsAsync(database).EnsureAsync(_now, CancellationToken.None);
        await new LogStore(database).WriteAsync([Record(_now, "today")], default);

        // The partition for yesterday exists, because partitions are made around today, but it holds nothing: the
        // installation started today.
        var usage = await Retention(database, new StorageOptions()).GetUsageAsync(_now, CancellationToken.None);

        Assert.Null(usage.DailyBytes);
        Assert.Null(usage.BudgetBytes);
        Assert.True(usage.UsedBytes > 0);
    }

    private static async Task<long> SizeAsync(NpgsqlDataSource database, params string[] tables)
    {
        long total = 0;

        foreach (var table in tables)
        {
            await using var command = database.CreateCommand($"SELECT pg_total_relation_size('{table}')");
            total += (long)(await command.ExecuteScalarAsync())!;
        }

        return total;
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
