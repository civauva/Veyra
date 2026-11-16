using Veyra.Storage.Schema;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Decides which level a metric read takes each part of its window from (ADR 0015): the coarsest rollup that still
/// gives its buckets, and the points wherever no rollup is complete.
/// </summary>
internal static class MetricReadPlan
{
    private static readonly TimeSpan _fiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);

    /// <summary>Splits a read into the parts read from each level, in time order.</summary>
    /// <param name="from">The start of the read, inclusive; rollups need it on a bucket boundary.</param>
    /// <param name="to">The end of the read, exclusive.</param>
    /// <param name="bucketSize">The bucket width.</param>
    /// <param name="fiveMinutesUpTo">Everything before this time is rolled up into 5-minute rows.</param>
    /// <param name="hoursUpTo">Everything before this time is rolled up into hourly rows.</param>
    /// <param name="unrolled">
    /// Times before those whose rollups are incomplete: days the backfill has not reached, and hours that received
    /// points after they were rolled up.
    /// </param>
    /// <returns>Adjacent segments that together cover the read exactly once.</returns>
    public static IReadOnlyList<MetricReadSegment> Split(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketSize,
        DateTimeOffset fiveMinutesUpTo,
        DateTimeOffset hoursUpTo,
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> unrolled)
    {
        // A rollup row can only be used when it falls into one bucket: buckets must be whole multiples of it and start
        // on its boundaries.
        var fiveMinutes = TimeBins.IsMultiple(bucketSize, _fiveMinutes) && TimeBins.Floor(from, bucketSize) == from;
        var hours = fiveMinutes && TimeBins.IsMultiple(bucketSize, _hour);

        if (!fiveMinutes)
        {
            return [new MetricReadSegment(from, to, MetricReadSource.Points)];
        }

        var hoursEnd = hours ? Max(from, TimeBins.Floor(Min(to, Min(hoursUpTo, fiveMinutesUpTo)), _hour)) : from;
        var fiveMinutesEnd = Max(hoursEnd, TimeBins.Floor(Min(to, fiveMinutesUpTo), _fiveMinutes));
        var segments = new[]
        {
            new MetricReadSegment(from, hoursEnd, MetricReadSource.Hours),
            new MetricReadSegment(hoursEnd, fiveMinutesEnd, MetricReadSource.FiveMinutes),
            new MetricReadSegment(fiveMinutesEnd, to, MetricReadSource.Points),
        };

        return Merge(segments.SelectMany(segment => CutOut(segment, unrolled)));
    }

    /// <summary>Hands the unrolled times of a rolled-up segment back to the points.</summary>
    private static IEnumerable<MetricReadSegment> CutOut(
        MetricReadSegment segment,
        IReadOnlyList<(DateTimeOffset From, DateTimeOffset To)> unrolled)
    {
        if (segment.Source == MetricReadSource.Points)
        {
            yield return segment;
            yield break;
        }

        var start = segment.From;

        foreach (var (gapFrom, gapTo) in unrolled.Where(gap => gap.From < segment.To && gap.To > segment.From)
            .OrderBy(gap => gap.From))
        {
            var cutFrom = Max(gapFrom, start);
            var cutTo = Min(gapTo, segment.To);

            if (cutFrom >= cutTo)
            {
                continue;
            }

            yield return segment with { From = start, To = cutFrom };
            yield return new MetricReadSegment(cutFrom, cutTo, MetricReadSource.Points);

            start = cutTo;
        }

        yield return segment with { From = start };
    }

    /// <summary>Drops empty segments and joins neighbours read from the same level.</summary>
    private static List<MetricReadSegment> Merge(IEnumerable<MetricReadSegment> segments)
    {
        var merged = new List<MetricReadSegment>();

        foreach (var segment in segments.Where(segment => segment.From < segment.To))
        {
            if (merged.Count > 0 && merged[^1].Source == segment.Source && merged[^1].To == segment.From)
            {
                merged[^1] = merged[^1] with { To = segment.To };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged;
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
