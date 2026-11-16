using Veyra.Agents.SqlServers;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;

namespace Veyra.Agents.Tests.SqlServers;

public class SqlServerSignalsTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid _server = new("0195a000-0000-7000-8000-000000000001");

    [Fact]
    public async Task ReadErrorsAsync_AsksForTheCollectorsRecentReaderErrors()
    {
        var logs = new FakeLogs();

        await new SqlServerSignals(logs, new FakeMetrics()).ReadErrorsAsync(_now, CancellationToken.None);

        var query = Assert.Single(logs.Queries);
        Assert.Equal(_now - SqlServerStatuses.ErrorsWithin, query.From);
        Assert.Equal(_now, query.To);
        Assert.Equal(LogSeverity.Error, query.MinimumSeverity);
        Assert.Equal(["otelcol-contrib"], query.Services);
        Assert.Equal("receiver", query.Attributes!["otelcol.component.kind"]);
    }

    [Fact]
    public async Task ReadErrorsAsync_Records_BecomeErrorsOfTheirServers()
    {
        var logs = new FakeLogs
        {
            Records =
            [
                Record($"sqlserver/{_server:N}", "Login failed for user 'veyra_monitor'."),
                Record("sqlserver", "a Collector Veyra did not set up"),
                Record($"sqlquery/{_server:N}", error: null),
            ],
        };

        var errors = await new SqlServerSignals(logs, new FakeMetrics())
            .ReadErrorsAsync(_now, CancellationToken.None);

        Assert.Equal(
            [
                new ReaderError(_server, "Login failed for user 'veyra_monitor'.", _now),
                new ReaderError(_server, "Error scraping metrics", _now),
            ],
            errors);
    }

    [Fact]
    public async Task LastDataAsync_RecentSeries_GivesItsLastSeenTime()
    {
        var metrics = new FakeMetrics { Series = [Series(_now.AddMinutes(-1))] };
        var server = Server();

        var last = await new SqlServerSignals(new FakeLogs(), metrics)
            .LastDataAsync(server, _now, CancellationToken.None);

        Assert.Equal(_now.AddMinutes(-1), last);
        var query = Assert.Single(metrics.Queries);
        Assert.Equal(new TelemetryBlock(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01"), query.Block);
        Assert.Equal(_now - SqlServerSignals.DataWithin, query.SeenSince);
    }

    [Fact]
    public async Task LastDataAsync_NoSeries_IsNull()
    {
        var last = await new SqlServerSignals(new FakeLogs(), new FakeMetrics())
            .LastDataAsync(Server(), _now, CancellationToken.None);

        Assert.Null(last);
    }

    private static SqlServer Server() =>
        new(_server, "sql-prod-01", "10.0.0.5", 1433, "production", Guid.Empty, "veyra_monitor",
            SqlServerEncryption.Required, _now, _now);

    private static StoredLogRecord Record(string component, string? error)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["otelcol.component.id"] = component,
            ["otelcol.component.kind"] = "receiver",
        };

        if (error is not null)
        {
            attributes["error"] = error;
        }

        return new StoredLogRecord(1, new LogRecord(
            Timestamp: _now,
            ObservedTimestamp: _now,
            Severity: LogSeverity.Error,
            SeverityText: "error",
            Body: "Error scraping metrics",
            ServiceName: "otelcol-contrib",
            Environment: null,
            TraceId: null,
            SpanId: null,
            Attributes: attributes,
            ResourceAttributes: new Dictionary<string, string>(StringComparer.Ordinal)));
    }

    private static MetricSeries Series(DateTimeOffset lastSeen) =>
        new(1, "sqlserver.cpu.count", MetricKind.Gauge, "1", "", MetricTemporality.Unspecified, false, null, null, null,
            new Dictionary<string, string>(), new Dictionary<string, string>(), lastSeen, lastSeen);

    private sealed class FakeLogs : ILogSearch
    {
        public List<LogQuery> Queries { get; } = [];

        public IReadOnlyList<StoredLogRecord> Records { get; init; } = [];

        public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);

            return Task.FromResult(new LogPage(Records, null));
        }

        public Task<LogSummary> SummarizeAsync(
            LogQuery query,
            TimeSpan bucketSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
            LogQuery query,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeMetrics : IMetricSearch
    {
        public List<MetricSeriesQuery> Queries { get; } = [];

        public IReadOnlyList<MetricSeries> Series { get; init; } = [];

        public Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(
            MetricSeriesQuery query,
            CancellationToken cancellationToken)
        {
            Queries.Add(query);

            return Task.FromResult(Series);
        }

        public Task<IReadOnlyList<MetricBucket>> ReadBucketsAsync(
            IReadOnlyList<long> seriesIds,
            DateTimeOffset windowStart,
            DateTimeOffset windowEnd,
            TimeSpan bucketSize,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MetricName>> ListNamesAsync(
            string? search,
            DateTimeOffset seenSince,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
