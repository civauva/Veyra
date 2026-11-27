using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Storage.Ingest;
using Veyra.Storage.Logs;
using Veyra.Storage.Metrics;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Ingest;

/// <summary>
/// The ingest counts of ADR 0023: every writer adds its batch in the batch's transaction, a read adds the minutes up
/// per step, and days older than 30 are dropped.
/// </summary>
[Collection(PostgresGroup.Name)]
public class IngestCountTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_noon);

    [Fact]
    public async Task WriteAsync_EachWriter_AddsItsBatchToTheCountsOfItsMinutes()
    {
        await using var database = await CreateAsync();

        await new LogStore(database).WriteAsync(
            [Log(_noon.AddSeconds(5)), Log(_noon.AddSeconds(50)), Log(_noon.AddMinutes(1))],
            default);
        await TestStores.Spans(database).WriteAsync([Span(Hex(1), _noon.AddSeconds(10))], default);
        await Metrics(database).WriteAsync(
            [Point(_noon.AddSeconds(1)), Point(_noon.AddSeconds(2)) with { Environment = "staging" }],
            default);

        Assert.Equal(
            [
                "12:00 production logs 2",
                "12:00 production points 1",
                "12:00 production spans 1",
                "12:00 staging points 1",
                "12:01 production logs 1",
            ],
            await RowsAsync(database));
    }

    [Fact]
    public async Task WriteAsync_SecondBatchOfTheSameMinute_AddsToItsRow()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);

        await store.WriteAsync([Log(_noon.AddSeconds(5))], default);
        await store.WriteAsync([Log(_noon.AddSeconds(6)), Log(_noon.AddSeconds(7))], default);

        Assert.Equal(["12:00 production logs 3"], await RowsAsync(database));
    }

    [Fact]
    public async Task WriteAsync_BatchThatFails_AddsNothing()
    {
        await using var database = await CreateAsync();
        var store = TestStores.Spans(database);
        await store.WriteAsync([Span(Hex(1), _noon)], default);

        // A span ID that is not hex fails the batch halfway, so neither its spans nor its counts are kept.
        await Assert.ThrowsAnyAsync<Exception>(
            () => store.WriteAsync([Span(Hex(2), _noon), Span("not hex", _noon)], default));

        Assert.Equal(["12:00 production spans 1"], await RowsAsync(database));
    }

    [Fact]
    public async Task CountAsync_MinuteSteps_AnswerEachMinute()
    {
        await using var database = await CreateAsync();
        await new LogStore(database).WriteAsync(
            [Log(_noon.AddMinutes(-2)), Log(_noon.AddMinutes(-1)), Log(_noon.AddMinutes(-1))],
            default);
        var counts = new IngestCountStore(database);

        var read = await counts.CountAsync(
            null,
            _noon.AddMinutes(-5),
            _noon.AddSeconds(30),
            TimeSpan.FromMinutes(1),
            default);

        Assert.Equal(
            [(_noon.AddMinutes(-2), 1L), (_noon.AddMinutes(-1), 2L)],
            read.OrderBy(count => count.Start).Select(count => (count.Start, count.Count)));
        Assert.All(read, count => Assert.Equal(IngestSignal.Logs, count.Signal));
    }

    [Fact]
    public async Task CountAsync_HourSteps_AddUpTheMinutes()
    {
        await using var database = await CreateAsync();
        await new LogStore(database).WriteAsync(
            [
                Log(_noon.AddHours(-2).AddMinutes(10)),
                Log(_noon.AddHours(-2).AddMinutes(50)),
                Log(_noon.AddMinutes(-3)),
            ],
            default);
        await Metrics(database).WriteAsync([Point(_noon.AddHours(-1).AddMinutes(30))], default);
        var counts = new IngestCountStore(database);

        var read = await counts.CountAsync(null, _noon.AddHours(-3), _noon, TimeSpan.FromHours(1), default);

        Assert.Equal(
            [
                (_noon.AddHours(-2), IngestSignal.Logs, 2L),
                (_noon.AddHours(-1), IngestSignal.Logs, 1L),
                (_noon.AddHours(-1), IngestSignal.Points, 1L),
            ],
            read.OrderBy(count => count.Start)
                .ThenBy(count => count.Signal)
                .Select(count => (count.Start, count.Signal, count.Count)));
    }

    [Fact]
    public async Task CountAsync_Environment_CountsOnlyIt()
    {
        await using var database = await CreateAsync();
        await Metrics(database).WriteAsync(
            [Point(_noon.AddMinutes(-1)), Point(_noon.AddMinutes(-1)) with { Environment = "staging" }],
            default);
        var counts = new IngestCountStore(database);

        var read = await counts.CountAsync("staging", _noon.AddHours(-1), _noon, TimeSpan.FromMinutes(5), default);

        var count = Assert.Single(read);
        Assert.Equal((_noon.AddMinutes(-5), IngestSignal.Points, 1L), (count.Start, count.Signal, count.Count));
    }

    [Fact]
    public async Task ApplyAsync_DaysOlderThanThirty_AreDroppedAndTheRestKept()
    {
        await using var database = await CreateAsync();
        var partitions = new IngestCountPartitions(
            database,
            _clock,
            NullLogger<IngestCountPartitions>.Instance);
        // Days from 26 October to 29 October and around today; 25 October lands in the default partition.
        await partitions.EnsureAsync(_noon.AddDays(-30), default);
        await partitions.EnsureAsync(_noon, default);
        await new LogStore(database).WriteAsync(
            [Log(_noon.AddDays(-32)), Log(_noon.AddDays(-31)), Log(_noon.AddDays(-30)), Log(_noon)],
            default);
        var retention = new IngestCountRetention(database, _clock, NullLogger<IngestCountRetention>.Instance);

        var result = await retention.ApplyAsync(default);

        Assert.Equal(["ingest_count_minute_20261026"], result.DroppedPartitions);
        Assert.Equal(1, result.DeletedDefaultRows);
        Assert.Equal(
            ["10-27 12:00 production logs 1", "11-26 12:00 production logs 1"],
            await RowsAsync(database, "MM-DD HH24:MI"));
    }

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private MetricStore Metrics(NpgsqlDataSource database) => new(
        database,
        new TestOptionsMonitor<StorageOptions>(new StorageOptions()),
        _clock,
        NullLogger<MetricStore>.Instance);

    private static async Task<List<string>> RowsAsync(NpgsqlDataSource database, string format = "HH24:MI")
    {
        await using var command = database.CreateCommand(
            """
            SELECT to_char(minute AT TIME ZONE 'UTC', $1) || ' ' || coalesce(environment, '-') || ' ' || signal
                || ' ' || count
            FROM storage.ingest_count_minute
            ORDER BY 1
            """);
        command.Parameters.AddWithValue(format);

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static string Hex(int span) => span.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    private static LogRecord Log(DateTimeOffset timestamp) => new(
        Timestamp: timestamp,
        ObservedTimestamp: timestamp,
        Severity: LogSeverity.Information,
        SeverityText: "Information",
        Body: "payment accepted",
        ServiceName: "payments",
        Environment: "production",
        TraceId: null,
        SpanId: null,
        Attributes: new Dictionary<string, string>(),
        ResourceAttributes: new Dictionary<string, string>());

    private static Span Span(string spanId, DateTimeOffset start) => new(
        "4bf92f3577b34da6a3ce929d0e0e4736",
        spanId,
        null,
        "GET /orders",
        SpanKind.Server,
        start,
        TimeSpan.FromMilliseconds(20),
        SpanStatus.Unset,
        null,
        "payments",
        "production",
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "payments" },
        [],
        [],
        SpanDropped.None);

    private static MetricPoint Point(DateTimeOffset at) => new(
        Name: "dotnet.gc.heap",
        Kind: MetricKind.Gauge,
        Unit: "By",
        Description: string.Empty,
        Temporality: MetricTemporality.Unspecified,
        Monotonic: false,
        ServiceName: "payments",
        Environment: "production",
        HostName: "web-01",
        ResourceAttributes: new Dictionary<string, string> { ["service.name"] = "payments" },
        Attributes: new Dictionary<string, string>(),
        Timestamp: at,
        StartTimestamp: null,
        Value: 1,
        Histogram: null);
}
