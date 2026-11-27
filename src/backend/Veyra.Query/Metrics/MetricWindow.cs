using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// A time window cut into equal buckets, ending in the bucket that holds its end. Charts get one point per bucket;
/// the bucket before the window is read too, as the baseline for rates (ADR 0010).
/// </summary>
/// <remarks>
/// The server chooses the step (ADR 0022): minutes for a window of up to six hours, read from the points and the
/// 5-minute rollups, and whole hours beyond that, read from the hourly rollups (ADR 0015). Buckets start on multiples
/// of their width, so a rollup row always falls into one bucket.
/// </remarks>
/// <param name="Range">The name of the range, for example <c>6h</c>, or <c>custom</c> for a from–to period.</param>
/// <param name="Start">The start of the first bucket.</param>
/// <param name="End">The end of the period: now, or the <c>to</c> asked for.</param>
/// <param name="Bucket">The width of a bucket.</param>
internal sealed record MetricWindow(string Range, DateTimeOffset Start, DateTimeOffset End, TimeSpan Bucket)
{
    /// <summary>The most points a line of an overview card has.</summary>
    public const int CardPoints = 72;

    /// <summary>The most points a line of a host, service, database, or browser chart has.</summary>
    public const int DetailPoints = 180;

    /// <summary>The longest window whose buckets are minutes; longer windows have buckets of whole hours.</summary>
    public static readonly TimeSpan MinutesUpTo = TimeSpan.FromHours(6);

    /// <summary>The named ranges a screen can ask for, with their lengths (ADR 0022).</summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> Ranges =
        new Dictionary<string, TimeSpan>(StringComparer.Ordinal)
        {
            ["15m"] = TimeSpan.FromMinutes(15),
            ["1h"] = TimeSpan.FromHours(1),
            ["6h"] = TimeSpan.FromHours(6),
            ["24h"] = TimeSpan.FromHours(24),
            ["7d"] = TimeSpan.FromDays(7),
            ["30d"] = TimeSpan.FromDays(30),
        };

    // The steps of windows up to six hours, and of longer ones. Each is a whole number of the rollup it reads.
    private static readonly TimeSpan[] _minuteSteps =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
    ];

    private static readonly TimeSpan[] _hourSteps =
    [
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(12),
        TimeSpan.FromHours(24),
    ];

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

    /// <summary>
    /// The start of the period asked for, which the first bucket may start a little after; responses state it as
    /// <c>from</c> (ADR 0022).
    /// </summary>
    public DateTimeOffset From { get; init; }

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

    /// <summary>
    /// The window of a screen's charts: its buckets are the shortest step that gives it no more than
    /// <see cref="DetailPoints"/> points.
    /// </summary>
    /// <param name="span">
    /// The period; a period that <see cref="MetricSpan.Resolve"/> refuses means the default range. Controllers refuse
    /// it with a problem before they get here.
    /// </param>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Of(MetricSpan span, DateTimeOffset now) => Over(span, now, DetailPoints);

    /// <summary>
    /// The window of the small lines of overview cards and resource lists: its buckets are the shortest step that
    /// gives it no more than <see cref="CardPoints"/> points.
    /// </summary>
    /// <remarks>
    /// A card line is 380 units wide and only decorates the numbers beside it, so more points draw nothing more. Fewer
    /// buckets mean fewer histograms to read for every service, which keeps the overview within its budget (ADR 0015).
    /// </remarks>
    /// <param name="span">The period, as for <see cref="Of"/>.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static MetricWindow Card(MetricSpan span, DateTimeOffset now) => Over(span, now, CardPoints);

    /// <summary>
    /// The step of a window: the shortest of 1, 5, 15, or 30 minutes up to six hours, or of 1, 3, 6, 12, or 24 hours
    /// beyond, that gives it no more than <paramref name="maxPoints"/> buckets.
    /// </summary>
    /// <param name="length">How long the window is.</param>
    /// <param name="maxPoints">The most buckets.</param>
    /// <returns>The width of a bucket.</returns>
    public static TimeSpan StepFor(TimeSpan length, int maxPoints)
    {
        var steps = length <= MinutesUpTo ? _minuteSteps : _hourSteps;

        return steps.FirstOrDefault(step => length / step <= maxPoints, steps[^1]);
    }

    /// <summary>
    /// The window stretched back by as many buckets as it has, so one read covers the period before it too, for the
    /// comparisons of ADR 0022. Buckets that start before <see cref="Start"/> of this window are the period before.
    /// </summary>
    /// <returns>The longer window.</returns>
    public MetricWindow AndBefore() => this with { Start = Start - (Bucket * Starts.Count) };

    private static MetricWindow Over(MetricSpan span, DateTimeOffset now, int maxPoints)
    {
        ArgumentNullException.ThrowIfNull(span);

        var period = span.Resolve(now) ?? MetricSpan.FromString(null).Resolve(now)!;

        return Ending(period.Name, period.To, period.Length, StepFor(period.Length, maxPoints));
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

        return new MetricWindow(MetricSpan.CustomRange, start, to, bucket) { From = from };
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

    // As many buckets as the length holds, rounded up, the last one holding the end. A length that is not a multiple of
    // the bucket still starts on a bucket boundary, so rollups can be read.
    private static MetricWindow Ending(string name, DateTimeOffset end, TimeSpan length, TimeSpan bucket)
    {
        var lastStart = new DateTimeOffset(end.UtcTicks - (end.UtcTicks % bucket.Ticks), TimeSpan.Zero);
        var buckets = (long)Math.Ceiling(length / bucket);

        return new MetricWindow(name, lastStart - (bucket * (buckets - 1)), end, bucket) { From = end - length };
    }
}
