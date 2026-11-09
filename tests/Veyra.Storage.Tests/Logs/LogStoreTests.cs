using Npgsql;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Logs;

[Collection(PostgresGroup.Name)]
public class LogStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_ThenSearch_ReturnsEveryFieldNewestFirst()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var older = Record(_noon.AddMinutes(-1), "first") with
        {
            Attributes = new Dictionary<string, string> { ["http.route"] = "/pay" },
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
        };
        var newer = Record(_noon, "second");

        await store.WriteAsync([older, newer], CancellationToken.None);
        var page = await store.SearchAsync(Window(), CancellationToken.None);

        Assert.Equal(["second", "first"], page.Items.Select(item => item.Record.Body));
        var stored = page.Items[1].Record;
        Assert.Equal(older.Timestamp, stored.Timestamp);
        Assert.Equal("/pay", stored.Attributes["http.route"]);
        Assert.Equal("payments", stored.ResourceAttributes["service.name"]);
        Assert.Equal("production", stored.Environment);
        Assert.Equal(older.TraceId, stored.TraceId);
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task SearchAsync_Filters_MatchSeverityServiceTextAndTrace()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        await store.WriteAsync(
            [
                Record(_noon, "payment declined by issuer") with { Severity = LogSeverity.Error },
                Record(_noon, "payment accepted") with { Severity = LogSeverity.Information },
                Record(_noon, "order declined") with { ServiceName = "orders", Severity = LogSeverity.Error },
                Record(_noon, "traced") with { TraceId = "aaaa0000aaaa0000aaaa0000aaaa0000" },
            ],
            CancellationToken.None);

        var bySeverity = await store.SearchAsync(Window() with { MinimumSeverity = LogSeverity.Warning }, default);
        var byService = await store.SearchAsync(Window() with { Services = ["orders"] }, default);
        var byText = await store.SearchAsync(Window() with { Text = "Declined PAYMENT" }, default);
        var byTrace = await store.SearchAsync(Window() with { TraceId = "aaaa0000aaaa0000aaaa0000aaaa0000" }, default);

        Assert.Equal(2, bySeverity.Items.Count);
        Assert.Equal("order declined", Assert.Single(byService.Items).Record.Body);
        Assert.Equal("payment declined by issuer", Assert.Single(byText.Items).Record.Body);
        Assert.Equal("traced", Assert.Single(byTrace.Items).Record.Body);
    }

    [Fact]
    public async Task SearchAsync_AttributeAndEnvironmentFilters_MatchExactly()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        await store.WriteAsync(
            [
                Record(_noon, "pay route") with { Attributes = Route("/pay") },
                Record(_noon, "order route") with { Attributes = Route("/order") },
                Record(_noon, "staging") with { Environment = "staging" },
            ],
            CancellationToken.None);

        var byAttribute = await store.SearchAsync(
            Window() with { Attributes = Route("/pay") },
            default);
        var byEnvironment = await store.SearchAsync(Window() with { Environments = ["staging"] }, default);

        Assert.Equal("pay route", Assert.Single(byAttribute.Items).Record.Body);
        Assert.Equal("staging", Assert.Single(byEnvironment.Items).Record.Body);
    }

    [Fact]
    public async Task SummarizeAsync_Window_CountsPerBucketWithErrorsAndPerService()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        await store.WriteAsync(
            [
                Record(_noon.AddMinutes(-50), "a"),
                Record(_noon.AddMinutes(-50), "b") with { Severity = LogSeverity.Error },
                Record(_noon.AddMinutes(-10), "c") with { ServiceName = "orders", Severity = LogSeverity.Fatal },
            ],
            CancellationToken.None);
        var window = Window() with { From = _noon.AddHours(-1), To = _noon, Services = ["payments"] };

        var summary = await store.SummarizeAsync(window, TimeSpan.FromMinutes(30), CancellationToken.None);

        // Buckets honour every filter, including the service; the service counts ignore the service filter,
        // so the filter panel can still offer the other services.
        Assert.Equal(2, summary.Buckets.Count);
        Assert.Equal(new LogBucket(_noon.AddHours(-1), 2, 1), summary.Buckets[0]);
        Assert.Equal(new LogBucket(_noon.AddMinutes(-30), 0, 0), summary.Buckets[1]);
        Assert.Equal([new ServiceCount("payments", 2), new ServiceCount("orders", 1)], summary.Services);
    }

    [Fact]
    public async Task SearchAsync_TimeWindow_ExcludesRecordsOutsideIt()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        await store.WriteAsync(
            [Record(_noon.AddHours(-2), "too old"), Record(_noon, "inside"), Record(_noon.AddHours(2), "too new")],
            CancellationToken.None);

        var page = await store.SearchAsync(Window(), CancellationToken.None);

        Assert.Equal("inside", Assert.Single(page.Items).Record.Body);
    }

    [Fact]
    public async Task SearchAsync_Cursor_PagesThroughEveryRecordExactlyOnce()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);

        // Equal timestamps on purpose: the record ID must break the tie.
        var records = Enumerable.Range(0, 7).Select(i => Record(_noon, $"record {i}")).ToList();
        await store.WriteAsync(records, CancellationToken.None);

        var seen = new List<string>();
        LogCursor? cursor = null;
        var pages = 0;

        do
        {
            var page = await store.SearchAsync(Window() with { Limit = 3, After = cursor }, CancellationToken.None);
            seen.AddRange(page.Items.Select(item => item.Record.Body));
            cursor = page.Next;
            pages++;
        }
        while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(records.Select(r => r.Body).Order(), seen.Order());
    }

    [Fact]
    public async Task EnsurePartitionsAsync_Today_StoresRecordsInTheDailyPartition()
    {
        await using var database = await CreateAsync();
        var partitions = new LogPartitions(
            database,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LogPartitions>.Instance);

        await partitions.EnsureAsync(_noon, CancellationToken.None);
        await new LogStore(database).WriteAsync([Record(_noon, "today")], CancellationToken.None);

        Assert.Equal("storage.log_record_20261109", await PartitionOfAsync(database, "today"));
    }

    [Fact]
    public async Task EnsurePartitionsAsync_FarFutureRecord_LandsInTheDefaultPartition()
    {
        await using var database = await CreateAsync();
        var partitions = new LogPartitions(
            database,
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LogPartitions>.Instance);

        await partitions.EnsureAsync(_noon, CancellationToken.None);
        await new LogStore(database).WriteAsync([Record(_noon.AddDays(30), "future")], CancellationToken.None);

        // A sender with a wrong clock must not lose data: the record is kept, just not in a daily partition.
        Assert.Equal("storage.log_record_default", await PartitionOfAsync(database, "future"));
    }

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private static Dictionary<string, string> Route(string route) => new() { ["http.route"] = route };

    private static LogQuery Window() => new(_noon.AddHours(-1), _noon.AddHours(1), null, null, null, null, 100, null);

    private static LogRecord Record(DateTimeOffset timestamp, string body) => new(
        Timestamp: timestamp,
        ObservedTimestamp: timestamp,
        Severity: LogSeverity.Information,
        SeverityText: "Information",
        Body: body,
        ServiceName: "payments",
        Environment: "production",
        TraceId: null,
        SpanId: null,
        Attributes: new Dictionary<string, string>(),
        ResourceAttributes: new Dictionary<string, string> { ["service.name"] = "payments" });

    private static async Task<string> PartitionOfAsync(NpgsqlDataSource database, string body)
    {
        await using var command = database.CreateCommand(
            "SELECT tableoid::regclass::text FROM storage.log_record WHERE body = $1");
        command.Parameters.AddWithValue(body);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
