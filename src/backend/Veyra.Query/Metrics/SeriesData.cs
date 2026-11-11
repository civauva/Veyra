using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>
/// Some series and their buckets over one window, with the calculations every view needs: rates of counters, levels
/// of gauges, and increases of histograms, each added up over a set of series.
/// </summary>
/// <param name="Window">The window the buckets cover.</param>
/// <param name="Series">The series.</param>
/// <param name="Buckets">The buckets of each series, oldest first.</param>
internal sealed record SeriesData(
    MetricWindow Window,
    IReadOnlyList<MetricSeries> Series,
    IReadOnlyDictionary<long, IReadOnlyList<MetricBucket>> Buckets)
{
    /// <summary>Data without any series.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The empty data.</returns>
    public static SeriesData Empty(MetricWindow window) =>
        new(window, [], new Dictionary<long, IReadOnlyList<MetricBucket>>());

    /// <summary>The same data, narrowed to some of its series.</summary>
    /// <param name="keep">Which series to keep.</param>
    /// <returns>The narrowed data.</returns>
    public SeriesData Where(Func<MetricSeries, bool> keep) => this with { Series = Series.Where(keep).ToList() };

    /// <summary>The series with one of the given names.</summary>
    /// <param name="names">The names.</param>
    /// <returns>The series.</returns>
    public IEnumerable<MetricSeries> Named(params string[] names) =>
        Series.Where(series => names.Contains(series.Name, StringComparer.Ordinal));

    /// <summary>The per-second rates of some monotonic sums, added up.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The rate per bucket start.</returns>
    public Dictionary<DateTimeOffset, double> Rates(IEnumerable<MetricSeries> series) =>
        MetricMath.Sum(series.Select(one => MetricMath.Rates(one, BucketsOf(one), Window.Bucket)));

    /// <summary>The levels of some gauges or sums that can fall, added up.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The level per bucket start.</returns>
    public Dictionary<DateTimeOffset, double> Levels(IEnumerable<MetricSeries> series) =>
        MetricMath.Sum(series.Select(one => MetricMath.Levels(BucketsOf(one))));

    /// <summary>What some cumulative histograms recorded, added up where their bounds agree.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The distribution per bucket start.</returns>
    public Dictionary<DateTimeOffset, Distribution> Increases(IEnumerable<MetricSeries> series) =>
        MetricMath.Combine(series.Select(one => MetricMath.Increases(BucketsOf(one))));

    /// <summary>
    /// When a host or service last sent: its newest point, or, without points in this window, the newest
    /// <c>last_seen</c>. Storage refreshes <c>last_seen</c> only once a minute, so the point is more precise.
    /// </summary>
    /// <param name="series">The host's or service's series, from every window read.</param>
    /// <param name="fallback">The time to answer without any series.</param>
    /// <returns>The time.</returns>
    public DateTimeOffset LastSent(IReadOnlyCollection<MetricSeries> series, DateTimeOffset fallback)
    {
        ArgumentNullException.ThrowIfNull(series);

        var seen = series.Count == 0 ? fallback : series.Max(one => one.LastSeen);
        var point = LastPoint(Series);

        return point is { } newest && newest > seen ? newest : seen;
    }

    /// <summary>The latest point time of some series.</summary>
    /// <param name="series">The series.</param>
    /// <returns>The time, or null without points.</returns>
    public DateTimeOffset? LastPoint(IEnumerable<MetricSeries> series) =>
        series.SelectMany(BucketsOf).Select(bucket => (DateTimeOffset?)bucket.LastTimestamp).Max();

    /// <summary>The average of values per bucket, inside the window only.</summary>
    /// <param name="values">The values per bucket start.</param>
    /// <returns>The average, or null without values.</returns>
    public double? Average(IReadOnlyDictionary<DateTimeOffset, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var inside = values.Where(pair => pair.Key >= Window.Start).Select(pair => pair.Value).ToList();

        return inside.Count == 0 ? null : inside.Average();
    }

    /// <summary>The value of the latest bucket inside the window.</summary>
    /// <param name="values">The values per bucket start.</param>
    /// <returns>The value, or null without values.</returns>
    public double? Latest(IReadOnlyDictionary<DateTimeOffset, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values.Where(pair => pair.Key >= Window.Start)
            .OrderBy(pair => pair.Key)
            .Select(pair => (double?)pair.Value)
            .LastOrDefault();
    }

    /// <summary>A distribution of every bucket inside the window added up.</summary>
    /// <param name="perBucket">The distribution per bucket start.</param>
    /// <returns>The total, or null without values.</returns>
    public Distribution? Total(IReadOnlyDictionary<DateTimeOffset, Distribution> perBucket)
    {
        ArgumentNullException.ThrowIfNull(perBucket);

        return perBucket.Where(pair => pair.Key >= Window.Start)
            .Select(pair => pair.Value)
            .Aggregate((Distribution?)null, (sum, next) => sum is null ? next : sum.Add(next));
    }

    /// <summary>The seconds the window's buckets cover, for rates over the whole window.</summary>
    public double Seconds => Window.Starts.Count * Window.Bucket.TotalSeconds;

    private IReadOnlyList<MetricBucket> BucketsOf(MetricSeries series) =>
        Buckets.TryGetValue(series.Id, out var buckets) ? buckets : [];
}
