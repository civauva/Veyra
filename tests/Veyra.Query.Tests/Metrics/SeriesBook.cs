using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;

namespace Veyra.Query.Tests.Metrics;

/// <summary>
/// Writes a SQL Server's series straight into the buckets of one window, its baseline bucket included, for the tests
/// of builders that turn read data into a response without a store.
/// </summary>
/// <param name="window">The window the buckets fill.</param>
internal sealed class SeriesBook(MetricWindow window)
{
    /// <summary>The server every series belongs to.</summary>
    public static readonly TelemetryBlock Server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

    private readonly List<MetricSeries> _series = [];

    private readonly Dictionary<long, IReadOnlyList<MetricBucket>> _buckets = [];

    /// <summary>The bucket starts the book fills, the baseline first.</summary>
    public IReadOnlyList<DateTimeOffset> Starts => [window.ReadStart, .. window.Starts];

    /// <summary>The data as a reader would return it.</summary>
    public SeriesData Data => new(window, _series, _buckets);

    /// <summary>A gauge at the same value in every bucket.</summary>
    public MetricSeries Level(string name, double value, params (string Key, string Value)[] attributes) =>
        Levels(name, _ => value, attributes);

    /// <summary>A gauge whose value per bucket start the test chooses; null leaves the bucket out.</summary>
    public MetricSeries Levels(
        string name,
        Func<DateTimeOffset, double?> valueAt,
        params (string Key, string Value)[] attributes)
    {
        var series = Add(name, MetricTemporality.Unspecified, attributes);

        _buckets[series.Id] = Starts
            .Select(start => (Start: start, Value: valueAt(start)))
            .Where(pair => pair.Value is not null)
            .Select(pair => Bucket(series, pair.Start, pair.Value!.Value))
            .ToList();

        return series;
    }

    /// <summary>A running total, as SQL Server keeps its counters, growing at a rate the test sets.</summary>
    public MetricSeries Total(
        string name,
        Func<DateTimeOffset, double> perSecondAt,
        params (string Key, string Value)[] attributes)
    {
        var series = Add(name, MetricTemporality.Cumulative, attributes);
        var total = 0d;
        var buckets = new List<MetricBucket>();

        foreach (var start in Starts)
        {
            total += perSecondAt(start) * window.Bucket.TotalSeconds;
            buckets.Add(Bucket(series, start, total));
        }

        _buckets[series.Id] = buckets;

        return series;
    }

    // Each bucket's last point sits at its middle, so consecutive buckets are one bucket apart.
    private MetricBucket Bucket(MetricSeries series, DateTimeOffset start, double value) =>
        new(series.Id, start, 1, value, value, start + (window.Bucket / 2), series.FirstSeen, value, null);

    private MetricSeries Add(
        string name,
        MetricTemporality temporality,
        (string Key, string Value)[] attributes)
    {
        var series = new MetricSeries(
            _series.Count + 1,
            name,
            temporality == MetricTemporality.Cumulative ? MetricKind.Sum : MetricKind.Gauge,
            "1",
            string.Empty,
            temporality,
            temporality == MetricTemporality.Cumulative,
            null,
            "production",
            null,
            new Dictionary<string, string>
            {
                ["db.system.name"] = "microsoft.sql_server",
                ["server.address"] = "sql-prod-01",
            },
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            window.ReadStart.AddDays(-1),
            window.End,
            Server);

        _series.Add(series);

        return series;
    }
}
