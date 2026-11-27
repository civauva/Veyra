using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Traces;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Traces;

/// <summary>
/// The trace queries meet their budgets by design (ADR 0014): the list reads only the root-span indexes of the days in
/// its window, and a trace is found through the trace ID index. These tests read the plans PostgreSQL makes, so a
/// change that loses an index or a partition bound fails here, not in production.
/// </summary>
[Collection(PostgresGroup.Name)]
public class SpanQueryPlanTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _today = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    // As on a real installation, the tables are too large to read whole, so PostgreSQL picks indexes.
    private static readonly (string, string)[] _largeTables = [("enable_seqscan", "off")];

    [Fact]
    public async Task CreateRootsCommand_LastHour_ReadsOnlyTodaysRootIndex()
    {
        await using var database = await CreateWeekAsync();
        var store = TestStores.Spans(database);
        var query = new TraceQuery(_today.AddHours(11), _today.AddHours(12));

        await using var command = store.CreateRootsCommand(query, "trace_id, name, duration_us", 51, paged: true);
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(["span_20261109"], plan.Relations.Distinct());
        Assert.NotEmpty(plan.Indexes);
        Assert.All(plan.Indexes, index => Assert.Contains("_start_time_span_id_", index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRootsCommand_OneServiceOverAWeek_ReadsTheServiceRootIndexOfEachDay()
    {
        await using var database = await CreateWeekAsync();
        var store = TestStores.Spans(database);
        var query = new TraceQuery(_today.AddDays(-6), _today.AddDays(1), Services: ["payments"], FailedOnly: true);

        await using var command = store.CreateRootsCommand(
            query,
            "start_time, duration_us, status",
            20_000,
            paged: false);
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(7, plan.Relations.Distinct().Count());
        Assert.DoesNotContain(plan.Relations, relation => relation.EndsWith("_default", StringComparison.Ordinal));
        Assert.NotEmpty(plan.Indexes);
        Assert.All(plan.Indexes, index => Assert.Contains("service_name", index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRootsCommand_WorstOrderOverAWeek_ReadsTheWorstFirstIndexOfEachDayInOrder()
    {
        await using var database = await CreateWeekAsync();
        var store = TestStores.Spans(database);
        var query = new TraceQuery(
            _today.AddDays(-6),
            _today.AddDays(1),
            Order: TraceOrder.Worst,
            AfterWorst: new TraceWorstCursor(
                true,
                TimeSpan.FromMilliseconds(120),
                SpanStoreTests.Trace(9),
                SpanStoreTests.Id(1)));

        await using var command = store.CreateRootsCommand(
            query,
            "trace_id, span_id, name, service_name, environment, start_time, duration_us, status",
            51,
            paged: true);

        // Without a sort the plan must read the roots in order, which only the worst-first index can do; a week is too
        // many roots to sort for each page on a real installation.
        var plan = await QueryPlan.ExplainAsync(database, command, [.. _largeTables, ("enable_sort", "off")]);

        Assert.Equal(7, plan.Relations.Distinct().Count());
        Assert.NotEmpty(plan.Indexes);
        Assert.All(plan.Indexes, index => Assert.Contains("_expr_duration_us_", index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateFiguresCommand_LastHour_ReadsOnlyTodaysRootIndexes()
    {
        await using var database = await CreateWeekAsync();
        var store = TestStores.Spans(database);
        var query = new TraceQuery(_today.AddHours(11), _today.AddHours(12), Services: ["payments"]);

        await using var command = store.CreateFiguresCommand(query, TimeSpan.FromMilliseconds(120));
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(["span_20261109"], plan.Relations.Distinct());
        Assert.NotEmpty(plan.Indexes);
        Assert.All(plan.Indexes, index => Assert.Contains("service_name", index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateTraceCommand_KnownDays_ReadsTheTraceIndexOfThoseDaysOnly()
    {
        await using var database = await CreateWeekAsync();
        var store = TestStores.Spans(database);

        await using var command = store.CreateTraceCommand(
            Convert.FromHexString(SpanStoreTests.Trace(3)),
            _today.AddDays(-2),
            _today.AddDays(1));
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(["span_20261107", "span_20261108", "span_20261109"], plan.Relations.Distinct().Order());
        Assert.All(plan.Indexes, index => Assert.Contains("trace_id", index, StringComparison.Ordinal));
    }

    /// <summary>A week of daily partitions, a trace of three spans every few minutes, analysed.</summary>
    private async Task<NpgsqlDataSource> CreateWeekAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new SpanPartitions(database, TimeProvider.System, NullLogger<SpanPartitions>.Instance);
        var spans = new List<Span>();

        for (var day = -7; day <= 1; day++)
        {
            await partitions.EnsureAsync(_today.AddDays(day), default);
        }

        for (var i = 0; i < 7 * 24 * 12; i++)
        {
            var start = _today.AddDays(-6).AddMinutes(i * 5);
            var trace = SpanStoreTests.Trace(i + 1);
            spans.Add(SpanStoreTests.Span(trace, SpanStoreTests.Id(1), null, "POST /payments", start, 120));
            spans.Add(SpanStoreTests.Span(trace, SpanStoreTests.Id(2), SpanStoreTests.Id(1), "SELECT", start, 10));
            spans.Add(SpanStoreTests.Span(trace, SpanStoreTests.Id(3), SpanStoreTests.Id(1), "GET psp", start, 90));
        }

        await TestStores.Spans(database).WriteAsync(spans, default);

        await using var analyze = database.CreateCommand("ANALYZE storage.span");
        await analyze.ExecuteNonQueryAsync();

        return database;
    }
}
