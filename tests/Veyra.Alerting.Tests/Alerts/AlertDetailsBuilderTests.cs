using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Api;
using Veyra.Alerting.Notifications;
using Veyra.Alerting.Tests.MetricRules;
using Veyra.Alerting.Tests.Notifications;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Alerts;

[Collection(PostgresGroup.Name)]
public class AlertDetailsBuilderTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _opened = new(2026, 11, 10, 6, 58, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(_opened.AddMinutes(36));
    private readonly SeriesLogs _logs = new();
    private readonly FakeMetricHealth _metrics = new();

    [Fact]
    public async Task BuildAsync_UnknownAlert_ReturnsNull()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        Assert.Null(await Builder(database).BuildAsync(Guid.CreateVersion7(), CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_OpenAlert_HasItsFactsTimelineWithDeliveriesAndTopErrors()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        await new ChannelStore(database).InsertAsync(Channels.Webhook("#ops-alerts"), CancellationToken.None);
        var alert = await OpenAsync(database);
        await new AlertStore(database).UpdateAsync(
            alert.Id,
            AlertSeverity.Warning,
            0.241,
            0.05,
            _opened.AddMinutes(14),
            CancellationToken.None);

        var details = await Builder(database).BuildAsync(alert.Id, CancellationToken.None);

        Assert.Equal(("payments-gateway", "production"), (details!.Service, details.Environment));
        Assert.Equal((0.241, _opened.AddMinutes(14)), (details.Peak, details.PeakAt));
        Assert.Equal(
            (0.01, 0.05, 300, 20),
            (details.CautionThreshold, details.WarningThreshold, details.WindowSeconds, details.MinimumRecords));
        Assert.Null(details.ResolvedAt);
        Assert.Equal([AlertEventKind.Opened, AlertEventKind.Escalated], details.Events.Select(e => e.Kind));
        var delivery = Assert.Single(details.Events[0].Deliveries);
        Assert.Equal(("#ops-alerts", DeliveryState.Pending), (delivery.ChannelName, delivery.State));
        Assert.Equal(_opened, delivery.NextAttemptAt);
        Assert.Equal("timeout", Assert.Single(details.TopErrors).Message);

        var errors = _logs.Grouped.Single();
        Assert.Equal(
            (_opened.AddMinutes(-5), _clock.GetUtcNow(), LogSeverity.Error),
            (errors.Query.From, errors.Query.To, errors.Query.MinimumSeverity));
        Assert.Equal(["payments-gateway"], errors.Query.Services);
        Assert.Equal(["production"], errors.Query.Environments);
    }

    [Fact]
    public async Task BuildAsync_Series_RunsFrom30MinutesBeforeOpeningToNowWithTheRollingRatio()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var alert = await OpenAsync(database);
        _logs.Minutes[_opened.AddMinutes(-1)] = (100, 0);
        _logs.Minutes[_opened] = (100, 50);

        var series = (await Builder(database).BuildAsync(alert.Id, CancellationToken.None))!.Series!;

        // One-minute points from 06:28 to 07:34; each covers the five minutes that end at it.
        Assert.Equal(60, series.BucketSeconds);
        Assert.Equal((_opened.AddMinutes(-30), _clock.GetUtcNow()), (series.From, series.To));
        Assert.Equal(66, series.Points.Count);
        Assert.Equal(_logs.Summaries.Single().Query.From, _opened.AddMinutes(-34));

        var beforeOpening = series.Points.Single(point => point.At == _opened);
        var atOpening = series.Points.Single(point => point.At == _opened.AddMinutes(1));
        var fiveLater = series.Points.Single(point => point.At == _opened.AddMinutes(5));
        var sixLater = series.Points.Single(point => point.At == _opened.AddMinutes(6));
        Assert.Equal((100L, 0L, 0.0), (beforeOpening.WindowTotal, beforeOpening.WindowErrors, beforeOpening.Ratio));
        Assert.Equal((200L, 50L, 0.25), (atOpening.WindowTotal, atOpening.WindowErrors, atOpening.Ratio));
        Assert.Equal(0.5, fiveLater.Ratio);
        Assert.Null(sixLater.Ratio);
    }

    [Fact]
    public async Task BuildAsync_ResolvedLongAgo_EndsTenMinutesAfterResolution()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var alert = await OpenAsync(database);
        await new AlertStore(database).ResolveAsync(alert.Id, 0, _opened.AddMinutes(20), CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(5));

        var details = await Builder(database).BuildAsync(alert.Id, CancellationToken.None);

        Assert.Equal(_opened.AddMinutes(30), details!.Series!.To);
        Assert.Equal(_opened.AddMinutes(20), _logs.Grouped.Single().Query.To);
    }

    [Fact]
    public async Task BuildAsync_LongAlert_UsesWiderBucketsAndAtMost240Points()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var alert = await OpenAsync(database);
        _clock.Advance(TimeSpan.FromHours(10));

        var series = (await Builder(database).BuildAsync(alert.Id, CancellationToken.None))!.Series!;

        Assert.Equal(180, series.BucketSeconds);
        Assert.True(series.Points.Count <= 240);
    }

    [Fact]
    public async Task BuildAsync_DiskAlert_HasTheDiskItsWeekAndTheHostsOtherDisks()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var alert = await OpenAsync(database) with { Id = Guid.CreateVersion7() };
        alert = alert with
        {
            Rule = AlertRules.DiskFilling,
            Subject = AlertSubject.Host("web-01", "/"),
            Value = 9.2,
            Peak = 9.2,
            Threshold = 14,
            Window = TimeSpan.FromDays(1),
        };
        await new AlertStore(database).InsertAsync(alert, CancellationToken.None);
        _metrics.Disks =
        [
            new DiskHealth("web-01", "production", "/boot", 22, 100, 0.22, null),
            new DiskHealth("web-01", "production", "/", 86, 100, 0.86, 9.2),
            new DiskHealth("db-01", "production", "/", 50, 100, 0.5, null),
        ];
        _metrics.DiskHistory = new DiskHistory(
            TimeSpan.FromHours(1),
            [new DiskUsagePoint(_opened.AddDays(-7), 0.75), new DiskUsagePoint(_opened, 0.81)]);

        var details = (await Builder(database).BuildAsync(alert.Id, CancellationToken.None))!;

        Assert.Equal(new AlertBlockResponse("host", "web-01", "/", "host:production/web-01"), details.Block);
        Assert.Null(details.Service);
        Assert.True(details.LowerIsWorse);
        Assert.Equal((14, 3, 21), (details.CautionThreshold, details.WarningThreshold, details.ClearThreshold));
        Assert.Null(details.MinimumRecords);
        Assert.Null(details.Series);
        Assert.Empty(details.TopErrors);
        Assert.Null(details.Latency);

        var disk = details.Disk!;
        Assert.Equal(("/", 0.86, 86.0, 100.0, 9.2), (disk.Mountpoint, disk.Ratio!.Value, disk.UsedBytes!.Value, disk.TotalBytes!.Value, disk.DaysUntilFull!.Value));
        Assert.Equal(3600, disk.BucketSeconds);
        Assert.Equal(2, disk.Points.Count);
        Assert.Equal(["/", "/boot"], disk.Disks.Select(other => other.Mountpoint));
        Assert.Equal(("web-01", "/", _opened.AddDays(-7), _clock.GetUtcNow()), _metrics.Reads.Single());
    }

    [Fact]
    public async Task BuildAsync_LatencyAlert_HasTheDurationsAndTheRoutesWhileItWasOpen()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var alert = (await OpenAsync(database)) with
        {
            Id = Guid.CreateVersion7(),
            Rule = AlertRules.LatencyP95,
            Subject = AlertSubject.Service("checkout-web"),
            Value = 3.4,
            Peak = 3.4,
            Threshold = 3,
        };
        await new AlertStore(database).InsertAsync(alert, CancellationToken.None);
        _metrics.LatencyHistory = new LatencyHistory(
            TimeSpan.FromMinutes(1),
            [new LatencyPoint(_opened, 0.4, 3.4, 600)],
            [new RouteLatency("POST", "/cart/checkout", 41, 4.1, 0.008)]);

        var details = (await Builder(database).BuildAsync(alert.Id, CancellationToken.None))!;

        Assert.Equal("checkout-web", details.Service);
        Assert.False(details.LowerIsWorse);
        Assert.Equal((1, 3, 0.8), (details.CautionThreshold, details.WarningThreshold, details.ClearThreshold));
        Assert.Equal(20, details.MinimumRequests);
        Assert.Null(details.Disk);
        Assert.Null(details.Series);

        var latency = details.Latency!;
        Assert.Equal(60, latency.BucketSeconds);
        Assert.Single(latency.Points);
        Assert.Equal("/cart/checkout", Assert.Single(latency.Routes).Route);
        Assert.Equal(
            [
                ("checkout-web", (string?)"production", _opened.AddMinutes(-30), _clock.GetUtcNow()),
                ("checkout-web", "production", _opened.AddMinutes(-5), _clock.GetUtcNow()),
            ],
            _metrics.Reads);
    }

    private static async Task<AlertState> OpenAsync(NpgsqlDataSource database)
    {
        var alert = new AlertState(
            Guid.CreateVersion7(),
            "error-rate",
            "production",
            AlertSubject.Service("payments-gateway"),
            AlertSeverity.Caution,
            0.018,
            0.018,
            0.01,
            TimeSpan.FromMinutes(5),
            _opened,
            _opened,
            null);

        await new AlertStore(database).InsertAsync(alert, CancellationToken.None);

        return alert;
    }

    private AlertDetailsBuilder Builder(NpgsqlDataSource database) => new(
        new AlertStore(database),
        new DeliveryStore(database),
        _logs,
        _metrics,
        Options.Create(new AlertingOptions()),
        _clock);

    /// <summary>Answers summaries per minute from counts a test sets, and one error group.</summary>
    private sealed class SeriesLogs : ILogSearch
    {
        public Dictionary<DateTimeOffset, (long Total, long Errors)> Minutes { get; } = [];

        public List<(LogQuery Query, TimeSpan Bucket)> Summaries { get; } = [];

        public List<(LogQuery Query, int Limit)> Grouped { get; } = [];

        public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken)
        {
            Summaries.Add((query, bucketSize));

            var buckets = new List<LogBucket>();

            for (var start = query.From; start < query.To; start += bucketSize)
            {
                var (total, errors) = Minutes.GetValueOrDefault(start);
                buckets.Add(new LogBucket(start, total, errors));
            }

            return Task.FromResult(new LogSummary(bucketSize, buckets, []));
        }

        public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
            LogQuery query,
            int limit,
            CancellationToken cancellationToken)
        {
            Grouped.Add((query, limit));

            return Task.FromResult<IReadOnlyList<LogMessageGroup>>(
                [new LogMessageGroup("timeout", 12, query.From, query.To)]);
        }
    }
}
