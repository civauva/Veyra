using Veyra.Storage.Schema;

namespace Veyra.Storage.Logs;

/// <summary>
/// Decides how to count the records of a window exactly and fast (ADR 0015): whole hours from the hour counts, whole
/// minutes from the minute counts, and only the seconds at either edge from the records.
/// </summary>
/// <remarks>
/// Buckets are aligned to the clock, not to the start of the window, so every bucket boundary inside the window falls
/// on a whole minute or hour and a count row never has to be split between two buckets. The first bucket starts at the
/// window's start and is shorter.
/// </remarks>
internal static class LogCountPlan
{
    private static readonly TimeSpan _minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan _day = TimeSpan.FromDays(1);

    /// <summary>Splits a window into the parts counted from each source, in time order.</summary>
    /// <param name="from">The start of the window, inclusive.</param>
    /// <param name="to">The end of the window, exclusive.</param>
    /// <param name="bucketSize">The histogram's bucket width, or <see langword="null"/> for one total.</param>
    /// <param name="countable">
    /// Whether the counts hold the query's filters: they do for time, severity, service, and environment, not for
    /// words, phrases, attributes, a trace, or a host.
    /// </param>
    /// <param name="uncounted">UTC days whose counts are incomplete, because the backfill has not reached them.</param>
    /// <returns>Adjacent segments that together cover the window exactly once.</returns>
    public static IReadOnlyList<LogCountSegment> Split(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan? bucketSize,
        bool countable,
        IReadOnlyCollection<DateOnly> uncounted)
    {
        // A minute row can only be used when no bucket boundary falls inside its minute; likewise an hour row.
        var minutes = countable && (bucketSize is not { } minuteSize || TimeBins.IsMultiple(minuteSize, _minute));
        var hours = minutes && (bucketSize is not { } hourSize || TimeBins.IsMultiple(hourSize, _hour));
        var firstMinute = TimeBins.Ceiling(from, _minute);
        var lastMinute = TimeBins.Floor(to, _minute);

        if (!minutes || firstMinute >= lastMinute)
        {
            return [new LogCountSegment(from, to, LogCountSource.Records)];
        }

        var segments = new List<LogCountSegment> { new(from, firstMinute, LogCountSource.Records) };
        var firstHour = TimeBins.Ceiling(firstMinute, _hour);
        var lastHour = TimeBins.Floor(lastMinute, _hour);

        if (hours && firstHour < lastHour)
        {
            segments.Add(new LogCountSegment(firstMinute, firstHour, LogCountSource.Minutes));
            segments.Add(new LogCountSegment(firstHour, lastHour, LogCountSource.Hours));
            segments.Add(new LogCountSegment(lastHour, lastMinute, LogCountSource.Minutes));
        }
        else
        {
            segments.Add(new LogCountSegment(firstMinute, lastMinute, LogCountSource.Minutes));
        }

        segments.Add(new LogCountSegment(lastMinute, to, LogCountSource.Records));

        return Merge(segments.SelectMany(segment => CutOut(segment, uncounted)));
    }

    /// <summary>
    /// Marks the parts of the record segments that fall on days whose words are not indexed by hour yet (ADR 0016), so
    /// they match words the old way.
    /// </summary>
    /// <param name="segments">The segments from <see cref="Split"/>.</param>
    /// <param name="unindexed">The days without the index; usually none.</param>
    /// <returns>The segments, with those parts as <see cref="LogCountSource.UnindexedRecords"/>.</returns>
    public static IReadOnlyList<LogCountSegment> SplitUnindexed(
        IReadOnlyList<LogCountSegment> segments,
        IReadOnlyCollection<DateOnly> unindexed)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (unindexed.Count == 0)
        {
            return segments;
        }

        return Merge(segments.SelectMany(segment => segment.Source == LogCountSource.Records
            ? Cut(segment, unindexed, LogCountSource.UnindexedRecords)
            : [segment]));
    }

    /// <summary>The index of the bucket a time falls into, from the bucket that holds the window's start.</summary>
    /// <param name="time">A time inside the window.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="bucketSize">The bucket width.</param>
    /// <returns>The index, 0 for the first bucket.</returns>
    public static long BucketOf(DateTimeOffset time, DateTimeOffset from, TimeSpan bucketSize) =>
        (TimeBins.Floor(time, bucketSize) - TimeBins.Floor(from, bucketSize)).Ticks / bucketSize.Ticks;

    /// <summary>The start of every bucket of a window: the window's start, then each clock-aligned boundary.</summary>
    /// <param name="from">The start of the window, inclusive.</param>
    /// <param name="to">The end of the window, exclusive.</param>
    /// <param name="bucketSize">The bucket width.</param>
    /// <returns>The starts, oldest first.</returns>
    public static IReadOnlyList<DateTimeOffset> BucketStarts(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan bucketSize)
    {
        var starts = new List<DateTimeOffset> { from };

        for (var start = TimeBins.Floor(from, bucketSize) + bucketSize; start < to; start += bucketSize)
        {
            starts.Add(start);
        }

        return starts;
    }

    /// <summary>Hands the uncounted days of a counted segment back to the records.</summary>
    private static IEnumerable<LogCountSegment> CutOut(
        LogCountSegment segment,
        IReadOnlyCollection<DateOnly> uncounted) =>
        segment.Source == LogCountSource.Records ? [segment] : Cut(segment, uncounted, LogCountSource.Records);

    /// <summary>Gives the parts of a segment that fall on the given days to another source.</summary>
    private static IEnumerable<LogCountSegment> Cut(
        LogCountSegment segment,
        IReadOnlyCollection<DateOnly> days,
        LogCountSource source)
    {
        var start = segment.From;

        for (var day = TimeBins.Floor(segment.From, _day); day < segment.To; day += _day)
        {
            if (!days.Contains(DateOnly.FromDateTime(day.UtcDateTime)))
            {
                continue;
            }

            var dayFrom = Max(day, segment.From);
            var dayTo = Min(day + _day, segment.To);

            yield return segment with { From = start, To = dayFrom };
            yield return new LogCountSegment(dayFrom, dayTo, source);

            start = dayTo;
        }

        yield return segment with { From = start };
    }

    /// <summary>Drops empty segments and joins neighbours read from the same source.</summary>
    private static List<LogCountSegment> Merge(IEnumerable<LogCountSegment> segments)
    {
        var merged = new List<LogCountSegment>();

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
