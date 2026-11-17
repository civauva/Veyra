using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Metrics.Health;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Reads Docker containers from the Collector's <c>docker_stats</c> receiver: CPU, memory of the limit, restarts, and
/// uptime (ADR 0018). A container is its block name on its host, since two hosts may run containers of one name.
/// </summary>
internal static class ContainerView
{
    // Docker shows the first 12 characters of a container's ID.
    private const int _shortId = 12;

    /// <summary>Whether a series belongs to a container, rather than to a service or a host.</summary>
    /// <param name="series">The series.</param>
    /// <returns><see langword="true"/> for a container's series.</returns>
    public static bool IsContainer(MetricSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return series.Block?.Kind == AlertBlockKind.Container;
    }

    /// <summary>A card per container, by name and then host.</summary>
    /// <param name="window">The containers' CPU and restart series over the chosen window.</param>
    /// <param name="recent">Their series over the last five minutes.</param>
    /// <returns>The cards.</returns>
    public static List<ContainerCardResponse> Cards(SeriesData window, SeriesData recent)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recent);

        return Keys(window.Series.Concat(recent.Series))
            .Select(key =>
            {
                var ofWindow = window.Where(series => key.Holds(series));
                var ofRecent = recent.Where(series => key.Holds(series));
                var any = ofWindow.Series.Concat(ofRecent.Series).ToList();
                var resource = any[0].ResourceAttributes;
                var memory = Memory(ofRecent);

                return new ContainerCardResponse(
                    Name: key.Name,
                    Host: key.Host,
                    Environment: any.Select(series => series.Environment).FirstOrDefault(value => value is not null),
                    Image: resource.GetValueOrDefault(MetricNames.ContainerImage),
                    Id: resource.GetValueOrDefault(MetricNames.ContainerId) is { Length: > 0 } id
                        ? id[..Math.Min(_shortId, id.Length)]
                        : null,
                    LastSeen: ofRecent.LastSent(any, window.Window.End),
                    CpuPercent: ofRecent.Average(ofRecent.Levels(ofRecent.Named(MetricNames.ContainerCpu))),
                    MemoryBytes: memory.Used,
                    MemoryLimitBytes: memory.Limit,
                    MemoryRatio: memory.Ratio,
                    Restarts: Increase(ofWindow, ofWindow.Named(MetricNames.ContainerRestarts)),
                    RestartsTotal: LastValue(ofRecent, MetricNames.ContainerRestarts),
                    UptimeSeconds: LastValue(ofRecent, MetricNames.ContainerUptime),
                    Cpu: window.Window.Line(
                        "cpu",
                        ofWindow.Levels(ofWindow.Named(MetricNames.ContainerCpu))));
            })
            .ToList();
    }

    /// <summary>Every container as the container rules judge it now.</summary>
    /// <param name="recent">The containers' series over the last five minutes.</param>
    /// <param name="quarter">Their restart series over the last 15 minutes in one-minute buckets.</param>
    /// <returns>One entry per container and host.</returns>
    public static List<ContainerHealth> Health(SeriesData recent, SeriesData quarter)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(quarter);

        return Keys(recent.Series.Concat(quarter.Series))
            .Select(key =>
            {
                var ofRecent = recent.Where(series => key.Holds(series));
                var memory = Memory(ofRecent);
                var restarts = quarter.Where(series => key.Holds(series)).Named(MetricNames.ContainerRestarts);

                return new ContainerHealth(
                    key.Name,
                    key.Host,
                    recent.Series.Concat(quarter.Series)
                        .Where(series => key.Holds(series))
                        .Select(series => series.Environment)
                        .FirstOrDefault(value => value is not null),
                    Increase(quarter, restarts),
                    memory.Ratio,
                    memory.Used,
                    memory.Limit);
            })
            .ToList();
    }

    /// <summary>
    /// What some running counts, such as Docker's restart count, rose by inside the window, from the last value of
    /// each bucket and the bucket before the window as the baseline. A count that fell started over, as when a
    /// container is created again, and counts from zero.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SeriesData.Rates"/>, a new start time is not a reset: Docker keeps its count across the
    /// container's restarts, which may give each point a new start time.
    /// </remarks>
    /// <param name="data">The series and their buckets.</param>
    /// <param name="series">The counts.</param>
    /// <returns>The rise, or <see langword="null"/> without any series.</returns>
    public static double? Increase(SeriesData data, IEnumerable<MetricSeries> series)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(series);

        var counts = series.ToList();

        if (counts.Count == 0)
        {
            return null;
        }

        var total = 0d;

        foreach (var count in counts)
        {
            var values = (data.Buckets.TryGetValue(count.Id, out var buckets) ? buckets : [])
                .Where(bucket => bucket.LastValue is not null)
                .ToList();

            for (var index = 1; index < values.Count; index++)
            {
                if (values[index].Start < data.Window.Start)
                {
                    continue;
                }

                var before = values[index - 1].LastValue!.Value;
                var after = values[index].LastValue!.Value;

                total += after >= before ? after - before : after;
            }
        }

        return total;
    }

    // The containers among the series, by name and then host.
    private static List<ContainerKey> Keys(IEnumerable<MetricSeries> series) =>
        series
            .Where(IsContainer)
            .Select(one => new ContainerKey(one.Block!.Name, one.HostName))
            .Distinct()
            .OrderBy(key => key.Name, StringComparer.Ordinal)
            .ThenBy(key => key.Host, StringComparer.Ordinal)
            .ToList();

    // Memory in use, its limit, and the share; the share from Docker's own percent when either is missing. Usage and
    // limit arrive as sums that can fall, so they are read as levels. A container caught between restarts reports 0
    // for both: it has no share then, rather than a division by zero or an alert.
    private static (double? Used, double? Limit, double? Ratio) Memory(SeriesData recent)
    {
        var used = recent.Latest(recent.Levels(recent.Named(MetricNames.ContainerMemoryUsage)));
        var limit = recent.Latest(recent.Levels(recent.Named(MetricNames.ContainerMemoryLimit)));
        var percent = recent.Latest(recent.Levels(recent.Named(MetricNames.ContainerMemoryPercent)));

        if (limit is <= 0)
        {
            return (null, null, null);
        }

        var ratio = used is { } inUse && limit is { } bytes ? inUse / bytes : percent / 100;

        return (used, limit, ratio);
    }

    // The last value sent inside the window, for counts and clocks whose average over a minute means nothing.
    private static double? LastValue(SeriesData data, string name) =>
        data.Named(name)
            .SelectMany(series => data.Buckets.TryGetValue(series.Id, out var buckets) ? buckets : [])
            .Where(bucket => bucket.Start >= data.Window.Start && bucket.LastValue is not null)
            .OrderBy(bucket => bucket.LastTimestamp)
            .Select(bucket => bucket.LastValue)
            .LastOrDefault();

    // A container on a host.
    private sealed record ContainerKey(string Name, string? Host)
    {
        public bool Holds(MetricSeries series) =>
            IsContainer(series) && series.Block!.Name == Name && series.HostName == Host;
    }
}
