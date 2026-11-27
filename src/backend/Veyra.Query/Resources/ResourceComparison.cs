using Veyra.Query.Metrics;

namespace Veyra.Query.Resources;

/// <summary>
/// Compares a figure with the period before of the same length, and cuts a window into the slices of a list's small
/// line (ADR 0022). Pure; tested without a database.
/// </summary>
internal static class ResourceComparison
{
    /// <summary>
    /// The change of a rate or an amount, such as requests or a latency, as a share of what it was: 0.12 is 12 % more.
    /// </summary>
    /// <param name="current">The figure of the window.</param>
    /// <param name="previous">The figure of the period before.</param>
    /// <returns>The change, or null when the period before has no data or was zero.</returns>
    public static double? Rate(double? current, double? previous) =>
        current is { } now && previous is { } before && before != 0 ? (now - before) / before : null;

    /// <summary>The change of a ratio in percentage points, as a ratio: 0.0012 is 0.12 points more.</summary>
    /// <param name="current">The ratio of the window.</param>
    /// <param name="previous">The ratio of the period before.</param>
    /// <returns>The difference, or null when either has no data.</returns>
    public static double? Points(double? current, double? previous) =>
        current is { } now && previous is { } before ? now - before : null;

    /// <summary>
    /// Cuts a window's counts into equal slices, each as a count per minute: the buckets that start in a slice, or,
    /// where the buckets are wider than the slices, the bucket the slice's middle falls in.
    /// </summary>
    /// <param name="window">The window, whose buckets the counts are of.</param>
    /// <param name="counts">How many there were per bucket start; a bucket without a value had no data.</param>
    /// <param name="slices">How many slices.</param>
    /// <returns>The count per minute of every slice, oldest first; null where nothing was measured.</returns>
    public static IReadOnlyList<double?> Slices(
        MetricWindow window,
        IReadOnlyDictionary<DateTimeOffset, double> counts,
        int slices)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(counts);

        var starts = window.Starts;
        var minutes = window.Bucket.TotalMinutes;
        var result = new double?[slices];

        if (starts.Count == 0 || slices <= 0)
        {
            return result;
        }

        var width = window.Bucket * starts.Count / slices;

        for (var slice = 0; slice < slices; slice++)
        {
            var from = window.Start + (width * slice);
            var to = from + width;
            var inside = starts.Where(start => start >= from && start < to).ToList();

            if (inside.Count == 0)
            {
                var middle = from + (width / 2);
                var index = Math.Min(starts.Count - 1, (int)((middle - window.Start) / window.Bucket));

                inside = [starts[index]];
            }

            var measured = inside.Where(counts.ContainsKey).ToList();

            result[slice] = measured.Count == 0
                ? null
                : measured.Sum(start => counts[start]) / (measured.Count * minutes);
        }

        return result;
    }
}
