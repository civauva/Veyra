using Veyra.Core.Metrics;
using Veyra.Query.Metrics;

namespace Veyra.Query.Home;

/// <summary>
/// What the home takes from metrics (VEY-19): the latency tile from the SDKs' request durations, and the disk outlook
/// from the Collector's filesystem usage. Without metrics both are empty and the home says what it needs.
/// </summary>
/// <param name="search">The stored metrics.</param>
internal sealed class HomeMetricsOutlook(IMetricSearch search)
{
    // Only senders that are still sending count.
    private static readonly TimeSpan _activeWithin = TimeSpan.FromMinutes(15);

    private readonly MetricReader _reader = new(search);

    /// <summary>Measures the latency of an environment over the last five minutes.</summary>
    /// <param name="environment">The environment, or null for all.</param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The latency, or null when no service sent request durations.</returns>
    public async Task<HomeLatencyResponse?> LatencyAsync(
        string? environment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.RequestDuration], Environment: environment),
            cancellationToken);
        var data = await _reader.ReadAsync(
            series.Where(one => one.ServiceName is not null).ToList(),
            MetricWindow.Recent(now),
            cancellationToken);

        var all = data.Total(data.Increases(data.Series));

        if (all?.Quantile(0.95) is not { } p95)
        {
            return null;
        }

        var perService = data.Series
            .GroupBy(one => one.ServiceName!, StringComparer.Ordinal)
            .Select(group => (Service: group.Key, P95: data.Total(data.Increases(group))?.Quantile(0.95)))
            .Where(entry => entry.P95 is not null)
            .OrderByDescending(entry => entry.P95)
            .ToList();
        var slowest = perService[0];

        return new HomeLatencyResponse(p95, slowest.Service, slowest.P95!.Value, perService.Count);
    }

    /// <summary>Lists every watched disk: those that fill soonest first, then the fullest.</summary>
    /// <param name="environment">
    /// The environment, or null for all. Hosts whose metrics name no environment count for every environment.
    /// </param>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The disks.</returns>
    public async Task<IReadOnlyList<HomeDiskResponse>> DisksAsync(
        string? environment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var series = (await search.FindSeriesAsync(
                new MetricSeriesQuery(now - _activeWithin, Names: [MetricNames.Filesystem]),
                cancellationToken))
            .Where(one => one.HostName is not null && one.ServiceName is null)
            .Where(one => environment is null || one.Environment is null || one.Environment == environment)
            .ToList();

        if (series.Count == 0)
        {
            return [];
        }

        var recent = await _reader.ReadAsync(series, MetricWindow.Recent(now), cancellationToken);
        var day = await _reader.ReadAsync(series, MetricWindow.Day(now), cancellationToken);

        return series
            .Select(one => one.HostName!)
            .Distinct(StringComparer.Ordinal)
            .SelectMany(host => HostView.Detail(
                    host,
                    recent.Where(one => one.HostName == host),
                    recent.Where(one => one.HostName == host),
                    day.Where(one => one.HostName == host))
                .Disks
                .Select(disk => new HomeDiskResponse(host, disk.Mountpoint, disk.Ratio, disk.DaysUntilFull)))
            .OrderBy(disk => disk.DaysUntilFull ?? double.MaxValue)
            .ThenByDescending(disk => disk.Ratio)
            .ToList();
    }
}
