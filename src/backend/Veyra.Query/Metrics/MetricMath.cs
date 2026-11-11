using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>
/// Turns stored buckets into what charts show (ADR 0010): rates of counters, levels of gauges, and increases of
/// histograms. Cumulative values are compared with the bucket before; a lower value or a new start time is a restart.
/// </summary>
internal static class MetricMath
{
    /// <summary>The per-second rate of a monotonic sum in each bucket.</summary>
    /// <param name="series">The series, for its temporality.</param>
    /// <param name="buckets">
    /// Its buckets, oldest first. The first cumulative bucket only serves as the baseline.
    /// </param>
    /// <param name="bucketSize">The width of a bucket, for delta sums.</param>
    /// <returns>The rate per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> Rates(
        MetricSeries series,
        IReadOnlyList<MetricBucket> buckets,
        TimeSpan bucketSize)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(buckets);

        var rates = new Dictionary<DateTimeOffset, double>();

        if (series.Temporality == MetricTemporality.Delta)
        {
            foreach (var bucket in buckets)
            {
                rates[bucket.Start] = (bucket.Total ?? 0) / bucketSize.TotalSeconds;
            }

            return rates;
        }

        for (var index = 1; index < buckets.Count; index++)
        {
            var previous = buckets[index - 1];
            var current = buckets[index];
            var seconds = (current.LastTimestamp - previous.LastTimestamp).TotalSeconds;

            if (seconds <= 0 || current.LastValue is not { } value || previous.LastValue is not { } before)
            {
                continue;
            }

            var increase = Restarted(previous, current, value < before) ? value : value - before;
            rates[current.Start] = increase / seconds;
        }

        return rates;
    }

    /// <summary>The average level of a gauge, or of a sum that can fall, in each bucket.</summary>
    /// <param name="buckets">The buckets.</param>
    /// <returns>The level per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> Levels(IReadOnlyList<MetricBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        return buckets
            .Where(bucket => bucket.Average is not null)
            .ToDictionary(bucket => bucket.Start, bucket => bucket.Average!.Value);
    }

    /// <summary>
    /// What a cumulative histogram recorded in each bucket: the last distribution minus the one of the bucket before.
    /// </summary>
    /// <param name="buckets">The buckets, oldest first. The first only serves as the baseline.</param>
    /// <returns>The increase per bucket start.</returns>
    public static Dictionary<DateTimeOffset, Distribution> Increases(IReadOnlyList<MetricBucket> buckets)
    {
        ArgumentNullException.ThrowIfNull(buckets);

        var increases = new Dictionary<DateTimeOffset, Distribution>();

        for (var index = 1; index < buckets.Count; index++)
        {
            var previous = buckets[index - 1].LastHistogram;
            var current = buckets[index].LastHistogram;

            if (previous is null || current is null || current.Counts.Count != current.Bounds.Count + 1)
            {
                continue;
            }

            var comparable = previous.Bounds.SequenceEqual(current.Bounds)
                && previous.Counts.Count == current.Counts.Count;
            var fell = comparable && current.Counts.Where((count, i) => count < previous.Counts[i]).Any();

            if (!comparable || Restarted(buckets[index - 1], buckets[index], fell))
            {
                increases[buckets[index].Start] = new Distribution(current.Bounds, [.. current.Counts], current.Sum);

                continue;
            }

            var counts = current.Counts.Select((count, i) => count - previous.Counts[i]).ToArray();
            var sum = current.Sum is { } after && previous.Sum is { } before ? after - before : (double?)null;

            increases[buckets[index].Start] = new Distribution(current.Bounds, counts, sum);
        }

        return increases;
    }

    /// <summary>Adds up the distributions of several series per bucket, where their bounds agree.</summary>
    /// <param name="perSeries">The increases of each series.</param>
    /// <returns>The combined distribution per bucket start.</returns>
    public static Dictionary<DateTimeOffset, Distribution> Combine(
        IEnumerable<Dictionary<DateTimeOffset, Distribution>> perSeries)
    {
        ArgumentNullException.ThrowIfNull(perSeries);

        var combined = new Dictionary<DateTimeOffset, Distribution>();

        foreach (var series in perSeries)
        {
            foreach (var (start, distribution) in series)
            {
                combined[start] = combined.TryGetValue(start, out var existing)
                    ? existing.Add(distribution)
                    : distribution;
            }
        }

        return combined;
    }

    /// <summary>Adds up several series' values per bucket.</summary>
    /// <param name="perSeries">The values of each series.</param>
    /// <returns>The sum per bucket start, for every bucket at least one series has.</returns>
    public static Dictionary<DateTimeOffset, double> Sum(IEnumerable<Dictionary<DateTimeOffset, double>> perSeries)
    {
        ArgumentNullException.ThrowIfNull(perSeries);

        var sums = new Dictionary<DateTimeOffset, double>();

        foreach (var series in perSeries)
        {
            foreach (var (start, value) in series)
            {
                sums[start] = sums.GetValueOrDefault(start) + value;
            }
        }

        return sums;
    }

    // A cumulative series restarted when its value fell or its start time changed.
    private static bool Restarted(MetricBucket previous, MetricBucket current, bool fell) =>
        fell || (previous.LastStart is { } before && current.LastStart is { } after && before != after);
}
