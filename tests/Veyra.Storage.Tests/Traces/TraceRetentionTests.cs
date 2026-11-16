using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Storage.Traces;
using Veyra.Testing;
using static Veyra.Storage.Tests.Traces.SpanStoreTests;

namespace Veyra.Storage.Tests.Traces;

/// <summary>Traces keep 7 days by default and may have a size budget, dropped by whole days (ADR 0014).</summary>
[Collection(PostgresGroup.Name)]
public class TraceRetentionTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_DaysOlderThanRetention_AreDropped()
    {
        await using var database = await CreateAsync(_now.AddDays(-9), _now);
        await new SpanStore(database).WriteAsync([Root(1, _now.AddDays(-9)), Root(2, _now)], default);

        var result = await Retention(database, new StorageOptions()).ApplyAsync(_now, default);

        Assert.Contains("span_20261031", result.DroppedPartitions);
        Assert.DoesNotContain("span_20261109", result.DroppedPartitions);
        Assert.Equal(["GET /2"], await NamesAsync(database));
    }

    [Fact]
    public async Task ApplyAsync_OldSpansInTheDefaultPartition_AreDeletedButFutureOnesKept()
    {
        await using var database = await CreateAsync(_now);
        await new SpanStore(database).WriteAsync([Root(1, _now.AddDays(-60)), Root(2, _now.AddDays(30))], default);

        var result = await Retention(database, new StorageOptions()).ApplyAsync(_now, default);

        Assert.Equal(1, result.DeletedDefaultRows);
        Assert.Equal(["GET /2"], await NamesAsync(database));
    }

    [Fact]
    public async Task ApplyAsync_OverTheSizeBudget_DropsTheOldestDaysButKeepsToday()
    {
        await using var database = await CreateAsync(_now.AddDays(-3), _now);
        await new SpanStore(database).WriteAsync(
            [Root(1, _now.AddDays(-3)), Root(2, _now.AddDays(-1)), Root(3, _now)],
            default);

        // A budget of about one kilobyte: far too small, so every day before today has to go.
        var tiny = new StorageOptions { MaxTraceStorageGigabytes = 0.000001 };
        var result = await Retention(database, tiny).ApplyAsync(_now, default);

        Assert.Equal("span_20261105", result.DroppedPartitions[0]);
        Assert.Equal(["GET /3"], await NamesAsync(database));
    }

    private static Core.Traces.Span Root(int number, DateTimeOffset start) =>
        Span(Trace(number), Id(number), null, $"GET /{number}", start, 10);

    private async Task<NpgsqlDataSource> CreateAsync(params DateTimeOffset[] around)
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new SpanPartitions(database, TimeProvider.System, NullLogger<SpanPartitions>.Instance);

        foreach (var time in around)
        {
            await partitions.EnsureAsync(time, default);
        }

        return database;
    }

    private static TraceRetention Retention(NpgsqlDataSource database, StorageOptions options) =>
        new(
            database,
            new TestOptionsMonitor<StorageOptions>(options),
            TimeProvider.System,
            NullLogger<TraceRetention>.Instance);

    private static async Task<List<string>> NamesAsync(NpgsqlDataSource database)
    {
        await using var command = database.CreateCommand("SELECT name FROM storage.span ORDER BY start_time");
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
