using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// A time window cut into equal buckets, ending in the bucket that holds now. Charts get one point per bucket; the
/// bucket before the window is read too, as the baseline for rates (ADR 0010).
/// </summary>
/// <param name="Range">The name of the range, for example <c>6h</c>.</param>
/// <param name="Start">The start of the first bucket.</param>
/// <param name="End">Now.</param>
/// <param name="Bucket">The width of a bucket.</param>
internal sealed record MetricWindow(string Range, DateTimeOffset Start, DateTimeOffset End, TimeSpan Bucket)
{
    /// <summary>The ranges a screen can ask for, with their bucket widths.</summary>
    public static readonly IReadOnlyDictionary<string, (TimeSpan Length, TimeSpan Bucket)> Ranges =
        new Dictionary<string, (TimeSpan, TimeSpan)>(StringComparer.Ordinal)
        {
            ["1h"] = (TimeSpan.FromHours(1), TimeSpan.FromMinutes(1)),
            ["6h"] = (TimeSpan.FromHours(6), TimeSpan.FromMinutes(5)),
            ["24h"] = (TimeSpan.FromHours(24), TimeSpan.FromMinutes(15)),
            ["7d"] = (TimeSpan.FromDays(7), TimeSpan.FromHours(1)),
        };

    private static readonly TimeSpan[] _chartBuckets =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

    /// <summary>Where reads start: one bucket before the window, as the baseline for cumulative values.</summary>
    public DateTimeOffset ReadStart => Start - Bucket;

    /// <summary>The start of every bucket of the window, oldest first.</summary>
    public IReadOnlyList<DateTimeOffset> Starts
    {
        get
        {
            var starts = new List<DateTimeOffset>();

            for (var start = Start; start < End; start += Bucket)
            {
                starts.Add(start);
            }

            return starts;
        }
    }

    /// <summary>A named range ending now.</summary>
    /// <param name="range">One of <see cref="Ranges"/>; anything else means <c>6h</c>.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Of(string? range, DateTimeOffset now)
    {
        var name = range is not null && Ranges.ContainsKey(range) ? range : "6h";
        var (length, bucket) = Ranges[name];

        return Ending(name, now, length, bucket);
    }

    /// <summary>The last five minutes in one-minute buckets, for the numbers that say "now".</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Recent(DateTimeOffset now) =>
        Ending("5m", now, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));

    /// <summary>The last day in hourly buckets, for trends such as when a disk is full.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Day(DateTimeOffset now) =>
        Ending("24h", now, TimeSpan.FromHours(24), TimeSpan.FromHours(1));

    /// <summary>
    /// A window over a given period, for an alert's chart: its buckets are the shortest of 1, 5, 15, or 30 minutes,
    /// or 1, 3, 6, 12, or 24 hours, that give it no more than <paramref name="maxPoints"/> points.
    /// </summary>
    /// <param name="from">The start of the period; the first bucket starts at or before it.</param>
    /// <param name="to">The end of the period.</param>
    /// <param name="maxPoints">The most buckets the window may have.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Covering(DateTimeOffset from, DateTimeOffset to, int maxPoints)
    {
        var span = to - from;
        var bucket = _chartBuckets.FirstOrDefault(
            candidate => span / candidate <= maxPoints,
            _chartBuckets[^1]);
        var start = new DateTimeOffset(from.UtcTicks - (from.UtcTicks % bucket.Ticks), TimeSpan.Zero);

        return new MetricWindow("custom", start, to, bucket);
    }

    /// <summary>Turns values per bucket into a chart line with a point for every bucket of the window.</summary>
    /// <param name="label">What the line shows.</param>
    /// <param name="values">The values per bucket start; missing buckets become null.</param>
    /// <returns>The line.</returns>
    public ChartLineResponse Line(string label, IReadOnlyDictionary<DateTimeOffset, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var points = Starts.Select(start => new ChartPointResponse(
                start,
                values.TryGetValue(start, out var value) && double.IsFinite(value) ? value : null))
            .ToList();

        return new ChartLineResponse(label, points.LastOrDefault(point => point.Value is not null)?.Value, points);
    }

    private static MetricWindow Ending(string name, DateTimeOffset now, TimeSpan length, TimeSpan bucket)
    {
        var lastStart = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % bucket.Ticks), TimeSpan.Zero);

        return new MetricWindow(name, lastStart - length + bucket, now, bucket);
    }
}
