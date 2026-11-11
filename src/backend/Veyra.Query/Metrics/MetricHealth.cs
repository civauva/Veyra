using Veyra.Core.Metrics;
using Veyra.Core.Metrics.Health;

namespace Veyra.Query.Metrics;

/// <summary>
/// The figures the Alerting module judges metrics by (ADR 0012), computed with the same views that draw the Metrics
/// screens and the home, so an alert and the screen it links to always agree.
/// </summary>
/// <param name="search">The stored metrics.</param>
internal sealed class MetricHealth(IMetricSearch search) : IMetricHealth
{
    // Only senders that are still sending count, as on the overview.
    private static readonly TimeSpan _activeWithin = TimeSpan.FromMinutes(15);

    // An alert's chart has no more points than this.
    private const int _maxPoints = 240;

    private readonly MetricReader _reader = new(search);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.Filesystem]),
                cancellationToken))
            .Where(one => one.HostName is not null && one.ServiceName is null)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var recent = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var day = await _reader.ReadAsync(series, MetricWindow.Day(now), cancellationToken);

        return series
            .GroupBy(one => one.HostName!, StringComparer.Ordinal)
            .SelectMany(host =>
            {
                var environment = host.Select(one => one.Environment).FirstOrDefault(value => value is not null);
                var ofHost = recent.Where(one => one.HostName == host.Key);

                return HostView.Detail(host.Key, ofHost, ofHost, day.Where(one => one.HostName == host.Key))
                    .Disks
                    .Select(disk => new DiskHealth(
                        host.Key,
                        environment,
                        disk.Mountpoint,
                        disk.UsedBytes,
                        disk.TotalBytes,
                        disk.Ratio,
                        disk.DaysUntilFull));
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceLatency>> ListLatenciesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.RequestDuration]),
                cancellationToken))
            .Where(one => one.ServiceName is not null)
            .ToList();
        var data = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);

        return series
            .GroupBy(one => (Service: one.ServiceName!, one.Environment))
            .Select(group =>
            {
                var total = data.Total(data.Increases(group));

                return new ServiceLatency(
                    group.Key.Service,
                    group.Key.Environment,
                    total?.Quantile(0.95),
                    total?.Count ?? 0);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<DiskHistory> ReadDiskAsync(
        string host,
        string mountpoint,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Covering(periodStart, periodEnd, _maxPoints);
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(window.ReadStart, Names: [MetricNames.Filesystem], HostName: host),
                cancellationToken))
            .Where(one => one.ServiceName is null)
            .ToList();
        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var share = HostView.DiskShare(data, mountpoint);

        return new DiskHistory(
            window.Bucket,
            window.Starts
                .Select(start => new DiskUsagePoint(start, share.TryGetValue(start, out var ratio) ? ratio : null))
                .ToList());
    }

    /// <inheritdoc />
    public async Task<LatencyHistory> ReadLatencyAsync(
        string service,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Covering(periodStart, periodEnd, _maxPoints);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                window.ReadStart,
                Names: [MetricNames.RequestDuration],
                ServiceName: service,
                Environment: environment),
            cancellationToken);
        var data = await _reader.ReadAsync(series, window, cancellationToken);
        var increases = data.Increases(data.Series);

        var points = window.Starts
            .Select(start => increases.TryGetValue(start, out var bucket)
                ? new LatencyPoint(start, bucket.Quantile(0.5), bucket.Quantile(0.95), bucket.Count)
                : new LatencyPoint(start, null, null, 0))
            .ToList();
        var routes = ServiceView.Routes(data)
            .OrderByDescending(route => route.P95Seconds ?? 0)
            .Select(route => new RouteLatency(
                route.Method,
                route.Route,
                route.RequestsPerSecond,
                route.P95Seconds,
                route.ServerErrorRatio))
            .ToList();

        return new LatencyHistory(window.Bucket, points, routes);
    }
}
