using Microsoft.Extensions.Options;
using Veyra.Alerting.Api;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Telemetry;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Puts together the alert details screen: the alert, its timeline with notifications, and the measure of its rule.
/// An error-rate alert shows the error rate around it and its most frequent errors; a disk alert the disk's week
/// and the host's other disks; a latency alert the service's request durations and its slowest routes.
/// </summary>
/// <param name="alerts">The alerts and their events.</param>
/// <param name="deliveries">The notifications about them.</param>
/// <param name="logs">The stored logs, for the error-rate chart and the top errors.</param>
/// <param name="metrics">The disk and latency figures, for the metric rules.</param>
/// <param name="options">The Alerting settings with the rules' thresholds.</param>
/// <param name="clock">The current time, which ends the chart of an open alert.</param>
internal sealed class AlertDetailsBuilder(
    AlertStore alerts,
    DeliveryStore deliveries,
    ILogSearch logs,
    IMetricHealth metrics,
    IOptions<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>How many error messages the details show.</summary>
    public const int TopErrorCount = 5;

    // The chart starts this long before the alert opened, to show what normal looked like.
    private static readonly TimeSpan _before = TimeSpan.FromMinutes(30);

    // And runs this long past the resolution, to show that it stayed resolved.
    private static readonly TimeSpan _after = TimeSpan.FromMinutes(10);

    // A disk changes slowly: its chart shows the week before the alert, and a day after it resolved.
    private static readonly TimeSpan _diskBefore = TimeSpan.FromDays(7);
    private static readonly TimeSpan _diskAfter = TimeSpan.FromDays(1);

    // Long alerts get wider buckets, so the chart never has more points than this.
    private const int _maxPoints = 240;

    /// <summary>Builds the details of one alert.</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The details, or <see langword="null"/> when there is no alert with this ID.</returns>
    public async Task<AlertDetailsResponse?> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await alerts.GetAsync(id, cancellationToken) is not { } alert)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var events = await alerts.ListEventsAsync(id, cancellationToken);
        var sent = await deliveries.ListForAlertAsync(id, cancellationToken);
        var peakAt = await alerts.GetPeakAtAsync(id, cancellationToken);
        var rules = RuleSettings.For(alert.Rule, options.Value);
        var worst = events.Select(e => e.Severity).Append(alert.Severity).Max();

        AlertSeriesResponse? series = null;
        IReadOnlyList<LogMessageGroup> topErrors = [];
        DiskDetailsResponse? disk = null;
        LatencyDetailsResponse? latency = null;

        switch (alert.Rule)
        {
            case AlertRules.DiskSpace or AlertRules.DiskFilling:
                disk = await DiskAsync(alert, now, cancellationToken);
                break;
            case AlertRules.LatencyP95:
                latency = await LatencyAsync(alert, now, cancellationToken);
                break;
            default:
                series = await SeriesAsync(alert, now, cancellationToken);

                // From the start of the window that opened the alert: the errors that set it off count too.
                var errorQuery = Query(alert, alert.OpenedAt - alert.Window, alert.ResolvedAt ?? now) with
                {
                    MinimumSeverity = LogSeverity.Error,
                };
                topErrors = await logs.GroupMessagesAsync(errorQuery, TopErrorCount, cancellationToken);
                break;
        }

        return new AlertDetailsResponse(
            Id: alert.Id,
            Rule: alert.Rule,
            Environment: alert.Environment,
            Block: AlertBlockResponse.Of(alert),
            Service: alert.Subject.Kind == AlertBlockKind.Service ? alert.Subject.Name : null,
            Severity: alert.Severity,
            Worst: worst,
            Value: alert.Value,
            Peak: alert.Peak,
            PeakAt: peakAt,
            Threshold: alert.Threshold,
            CautionThreshold: rules.Caution,
            WarningThreshold: rules.Warning,
            ClearThreshold: rules.Clear,
            LowerIsWorse: AlertRules.LowerIsWorse(alert.Rule),
            WindowSeconds: (int)alert.Window.TotalSeconds,
            MinimumRecords: alert.Rule == AlertRules.ErrorRate ? options.Value.ErrorRate.MinimumRecords : null,
            MinimumRequests: alert.Rule == AlertRules.LatencyP95 ? options.Value.Latency.MinimumRequests : null,
            OpenedAt: alert.OpenedAt,
            UpdatedAt: alert.UpdatedAt,
            ResolvedAt: alert.ResolvedAt,
            Events: events.Select(e => new AlertEventResponse(
                    e.Kind,
                    e.Severity,
                    e.Value,
                    e.At,
                    sent.Where(d => d.EventId == e.Id).Select(Delivery).ToList()))
                .ToList(),
            Series: series,
            TopErrors: topErrors,
            Disk: disk,
            Latency: latency);
    }

    private async Task<DiskDetailsResponse> DiskAsync(
        AlertState alert,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var host = alert.Subject.Name;
        var mountpoint = alert.Subject.Part ?? string.Empty;
        var from = alert.OpenedAt - _diskBefore;
        var to = alert.ResolvedAt is { } resolvedAt && resolvedAt + _diskAfter < now ? resolvedAt + _diskAfter : now;

        var history = await metrics.ReadDiskAsync(host, mountpoint, from, to, cancellationToken);
        var disks = (await metrics.ListDisksAsync(now, cancellationToken))
            .Where(disk => disk.Host == host)
            .OrderByDescending(disk => disk.Ratio)
            .ToList();
        var current = disks.FirstOrDefault(disk => disk.Mountpoint == mountpoint);

        return new DiskDetailsResponse(
            mountpoint,
            current?.Ratio,
            current?.UsedBytes,
            current?.TotalBytes,
            current?.DaysUntilFull,
            (int)history.Bucket.TotalSeconds,
            history.Points.Count > 0 ? history.Points[0].At : from,
            to,
            history.Points,
            disks.Select(disk => new HostDiskResponse(disk.Mountpoint, disk.Ratio, disk.DaysUntilFull)).ToList());
    }

    private async Task<LatencyDetailsResponse> LatencyAsync(
        AlertState alert,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var service = alert.Subject.Name;
        var end = alert.ResolvedAt is { } resolvedAt && resolvedAt + _after < now ? resolvedAt + _after : now;

        var chart = await metrics.ReadLatencyAsync(
            service,
            alert.Environment,
            alert.OpenedAt - _before,
            end,
            cancellationToken);

        // The routes only while it was open, from the start of the window that opened it.
        var during = await metrics.ReadLatencyAsync(
            service,
            alert.Environment,
            alert.OpenedAt - alert.Window,
            alert.ResolvedAt ?? now,
            cancellationToken);

        return new LatencyDetailsResponse(
            (int)chart.Bucket.TotalSeconds,
            chart.Points.Count > 0 ? chart.Points[0].At : alert.OpenedAt - _before,
            end,
            chart.Points,
            during.Routes);
    }

    private async Task<AlertSeriesResponse> SeriesAsync(
        AlertState alert,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var end = alert.ResolvedAt is { } resolvedAt && resolvedAt + _after < now ? resolvedAt + _after : now;
        var bucket = BucketFor(end - (alert.OpenedAt - _before));
        var start = Floor(alert.OpenedAt - _before, bucket);

        // Each point is the ratio over the rule's window ending at that point, as the rule measured it. The first
        // points need the buckets before the chart starts.
        var windowBuckets = Math.Max(1, (int)Math.Round(alert.Window / bucket));
        var queryStart = start - (bucket * (windowBuckets - 1));

        var summary = await logs.SummarizeAsync(Query(alert, queryStart, end), bucket, cancellationToken);
        var buckets = summary.Buckets;
        var points = new List<AlertPointResponse>();

        for (var index = windowBuckets - 1; index < buckets.Count; index++)
        {
            var window = buckets.Skip(index - windowBuckets + 1).Take(windowBuckets).ToList();
            var total = window.Sum(b => b.Total);
            var errors = window.Sum(b => b.Errors);

            points.Add(new AlertPointResponse(
                At: buckets[index].Start + bucket,
                Total: buckets[index].Total,
                Errors: buckets[index].Errors,
                WindowTotal: total,
                WindowErrors: errors,
                Ratio: total == 0 ? null : (double)errors / total));
        }

        return new AlertSeriesResponse((int)bucket.TotalSeconds, start, end, points);
    }

    private static TimeSpan BucketFor(TimeSpan span)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes / _maxPoints));

        return TimeSpan.FromMinutes(minutes);
    }

    private static DateTimeOffset Floor(DateTimeOffset at, TimeSpan bucket) =>
        new(at.UtcTicks - (at.UtcTicks % bucket.Ticks), TimeSpan.Zero);

    // The alert's own service and environment. Records that name no environment cannot be told apart from the
    // others in a query, so an alert without one counts every environment of its service.
    private static LogQuery Query(AlertState alert, DateTimeOffset from, DateTimeOffset to) => new(
        From: from,
        To: to,
        MinimumSeverity: null,
        Services: [alert.Subject.Name],
        Text: null,
        TraceId: null,
        Limit: 1,
        After: null,
        Environments: alert.Environment is null ? null : [alert.Environment]);

    private static DeliveryResponse Delivery(Delivery delivery) => new(
        delivery.ChannelId,
        delivery.ChannelName,
        delivery.ChannelKind,
        delivery.State,
        delivery.Attempts,
        delivery.LastAttemptAt,
        delivery.LastStatus,
        delivery.LastError,
        delivery.State == DeliveryState.Pending ? delivery.NextAttemptAt : null,
        delivery.SentAt);
}
