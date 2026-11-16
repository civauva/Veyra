using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Traces;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Traces;

/// <summary>
/// Spans are stored one row each and read back as traces (ADR 0014). These tests prove a span comes back exactly as
/// written, and that the trace list pages, filters, and counts as the trace screens need.
/// </summary>
[Collection(PostgresGroup.Name)]
public class SpanStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_ThenGetTraceAsync_ReturnsEverySpanAsWritten()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var root = Span(Trace(1), Id(1), null, "POST /api/checkout", _day.AddHours(10), 1840, SpanStatus.Error) with
        {
            StatusMessage = "502 from payments",
            HostName = "web-02",
            Attributes = new Dictionary<string, string> { ["http.route"] = "/api/checkout" },
            Events = [new SpanEvent(_day.AddHours(10).AddSeconds(1), "exception", Exception("Timed out"))],
            Links = [new SpanLink(Trace(9), Id(9), new Dictionary<string, string> { ["kind"] = "batch" })],
            Dropped = new SpanDropped(3, 2, 1),
        };
        var child = Span(Trace(1), Id(2), Id(1), "SELECT orders", _day.AddHours(10).AddMilliseconds(5), 12);
        await store.WriteAsync([child, root], default);

        var trace = await store.GetTraceAsync(Trace(1), around: null, default);

        Assert.Equal(2, trace.Count);
        AssertSame(root, trace[0]);
        AssertSame(child, trace[1]);
    }

    [Fact]
    public async Task GetTraceAsync_KnownTime_FindsTheTraceOnItsDayAndAnUnknownTraceIsEmpty()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        await store.WriteAsync([Span(Trace(1), Id(1), null, "GET /", _day.AddDays(-1).AddHours(23), 5)], default);

        Assert.Single(await store.GetTraceAsync(Trace(1), around: _day.AddHours(1), default));
        Assert.Single(await store.GetTraceAsync(Trace(1), around: null, default));
        Assert.Empty(await store.GetTraceAsync(Trace(2), around: _day, default));
    }

    [Fact]
    public async Task ListAsync_ManyTraces_PagesNewestFirstByKeyset()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var roots = Enumerable.Range(1, 7)
            .Select(i => Span(Trace(i), Id(i), null, $"GET /{i}", _day.AddHours(8).AddMinutes(i % 3), 10))
            .ToList();
        await store.WriteAsync(roots, default);
        var query = new TraceQuery(_day, _day.AddDays(1), Limit: 3);

        var seen = new List<string>();
        TraceCursor? cursor = null;

        do
        {
            var page = await store.ListAsync(query with { After = cursor }, default);
            seen.AddRange(page.Items.Select(item => item.Name));
            cursor = page.Next;
        }
        while (cursor is not null);

        var expected = roots
            .OrderByDescending(root => root.Start)
            .ThenByDescending(root => root.SpanId, StringComparer.Ordinal)
            .Select(root => root.Name);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task ListAsync_Filters_KeepOnlyTheMatchingRoots()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var at = _day.AddHours(9);
        await store.WriteAsync(
            [
                Span(Trace(1), Id(1), null, "POST /api/checkout", at, 1800, SpanStatus.Error),
                Span(Trace(2), Id(2), null, "POST /api/checkout", at.AddMinutes(1), 90) with { ServiceName = "orders" },
                Span(Trace(3), Id(3), null, "GET /health", at.AddMinutes(2), 2) with { Environment = "staging" },
                Span(Trace(4), Id(4), null, "POST /payments", at.AddMinutes(3), 400) with
                {
                    Attributes = new Dictionary<string, string> { ["http.route"] = "/payments" },
                },
                Span(Trace(5), Id(5), Id(9), "POST /api/checkout", at.AddMinutes(4), 2000, SpanStatus.Error),
            ],
            default);
        var all = new TraceQuery(_day, _day.AddDays(1));

        async Task<string[]> TracesAsync(TraceQuery query) =>
            (await store.ListAsync(query, default)).Items.Select(item => item.TraceId).Order().ToArray();

        Assert.Equal([Trace(1), Trace(2), Trace(3), Trace(4)], await TracesAsync(all));
        Assert.Equal([Trace(2)], await TracesAsync(all with { Services = ["orders"] }));
        Assert.Equal([Trace(3)], await TracesAsync(all with { Environments = ["staging"] }));
        Assert.Equal([Trace(1)], await TracesAsync(all with { FailedOnly = true }));
        Assert.Equal(
            [Trace(1), Trace(4)],
            await TracesAsync(all with { MinDuration = TimeSpan.FromMilliseconds(400) }));
        Assert.Equal([Trace(3)], await TracesAsync(all with { MaxDuration = TimeSpan.FromMilliseconds(50) }));
        Assert.Equal([Trace(1), Trace(2)], await TracesAsync(all with { Name = "CHECKOUT" }));
        Assert.Equal(
            [Trace(4)],
            await TracesAsync(all with
            {
                Attributes = new Dictionary<string, string> { ["http.route"] = "/payments" },
            }));
    }

    [Fact]
    public async Task ListAsync_TraceWithChildren_CountsSpansFailuresServicesAndFindsTheCause()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var at = _day.AddHours(10);
        await store.WriteAsync(
            [
                Span(Trace(1), Id(1), null, "POST /api/checkout", at, 1840, SpanStatus.Error) with
                {
                    ServiceName = "checkout-web",
                    StatusMessage = "502",
                },
                Span(Trace(1), Id(2), Id(1), "POST /payments", at.AddMilliseconds(50), 1780, SpanStatus.Error),
                Span(Trace(1), Id(3), Id(2), "POST /authorize", at.AddMilliseconds(100), 1600, SpanStatus.Error) with
                {
                    ServiceName = "psp-client",
                    Events = [new SpanEvent(at.AddSeconds(1.7), "exception", Exception("The PSP did not answer"))],
                },
                Span(Trace(1), Id(4), Id(2), "SELECT payments", at.AddMilliseconds(60), 14),
            ],
            default);

        var trace = Assert.Single((await store.ListAsync(new TraceQuery(_day, _day.AddDays(1)), default)).Items);

        Assert.Equal(("POST /api/checkout", "checkout-web", true), (trace.Name, trace.ServiceName, trace.Failed));
        Assert.Equal((4, 3), (trace.Spans, trace.FailedSpans));
        Assert.Equal(new TraceFailure("psp-client", "POST /authorize", "The PSP did not answer"), trace.Cause);
        Assert.Equal(
            [
                new TraceServiceShare("payments", 2),
                new TraceServiceShare("checkout-web", 1),
                new TraceServiceShare("psp-client", 1),
            ],
            trace.Services);
    }

    [Fact]
    public async Task ListAsync_DeepestFailureHasNoException_TakesTheCauseFromTheSpanWithTheException()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var at = _day.AddHours(11);
        await store.WriteAsync(
            [
                Span(Trace(1), Id(1), null, "POST /payments", at, 1800, SpanStatus.Error),
                Span(Trace(1), Id(2), Id(1), "POST /authorize", at.AddMilliseconds(10), 1600, SpanStatus.Error) with
                {
                    ServiceName = "psp-client",
                    Events = [new SpanEvent(at.AddSeconds(1.6), "exception", Exception("The PSP did not answer"))],
                },
                Span(Trace(1), Id(3), Id(2), "HTTPS POST psp", at.AddMilliseconds(20), 1500, SpanStatus.Error) with
                {
                    ServiceName = "psp-client",
                    StatusMessage = "timeout",
                },
            ],
            default);

        var trace = Assert.Single((await store.ListAsync(new TraceQuery(_day, _day.AddDays(1)), default)).Items);

        // The bare "timeout" below it says less than the exception that explains it.
        Assert.Equal(new TraceFailure("psp-client", "POST /authorize", "The PSP did not answer"), trace.Cause);
    }

    [Fact]
    public async Task DurationsAsync_BusyWindow_KeepsEveryFailedAndSlowTraceAndAnEvenSample()
    {
        await using var database = await CreateAsync();
        var store = new SpanStore(database);
        var roots = Enumerable.Range(1, 400).Select(i => Span(
                Trace(i),
                Id(i),
                null,
                "GET /",
                _day.AddHours(8).AddSeconds(i),
                i % 15 == 0 ? 2000 : 100 + (i % 10),
                i % 97 == 0 ? SpanStatus.Error : SpanStatus.Unset))
            .ToList();
        await store.WriteAsync(roots, default);

        var durations = await store.DurationsAsync(new TraceQuery(_day, _day.AddDays(1)), maxPoints: 60, default);

        Assert.Equal(400, durations.Read);
        Assert.True(durations.Points.Count <= 60);
        Assert.Equal(4, durations.Points.Count(point => point.Failed));
        Assert.Equal(26, durations.Points.Count(point => point.Duration >= TimeSpan.FromMilliseconds(2000)));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), durations.P95);
    }

    internal static string Trace(int number) =>
        number.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);

    internal static string Id(int number) => number.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    internal static Span Span(
        string traceId,
        string spanId,
        string? parentId,
        string name,
        DateTimeOffset start,
        int milliseconds,
        SpanStatus status = SpanStatus.Unset) => new(
        traceId,
        spanId,
        parentId,
        name,
        parentId is null ? SpanKind.Server : SpanKind.Client,
        start,
        TimeSpan.FromMilliseconds(milliseconds),
        status,
        null,
        "payments",
        "production",
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "payments" },
        [],
        [],
        SpanDropped.None);

    private static Dictionary<string, string> Exception(string message) => new()
    {
        ["exception.type"] = "System.TimeoutException",
        ["exception.message"] = message,
    };

    private static void AssertSame(Span expected, Span actual)
    {
        Assert.Equal(
            expected with { Attributes = null!, ResourceAttributes = null!, Events = null!, Links = null! },
            actual with { Attributes = null!, ResourceAttributes = null!, Events = null!, Links = null! });
        Assert.Equal(expected.Attributes, actual.Attributes);
        Assert.Equal(expected.ResourceAttributes, actual.ResourceAttributes);
        Assert.Equal(
            expected.Events.Select(@event => (@event.Timestamp, @event.Name)),
            actual.Events.Select(@event => (@event.Timestamp, @event.Name)));
        Assert.Equal(expected.Events.Select(e => e.Attributes.Count), actual.Events.Select(e => e.Attributes.Count));
        Assert.Equal(
            expected.Links.Select(link => (link.TraceId, link.SpanId)),
            actual.Links.Select(link => (link.TraceId, link.SpanId)));
    }

    private async Task<NpgsqlDataSource> CreateAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        await new SpanPartitions(database, TimeProvider.System, NullLogger<SpanPartitions>.Instance)
            .EnsureAsync(_day, default);

        return database;
    }
}
