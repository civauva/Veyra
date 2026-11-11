using Veyra.Core.Telemetry;

namespace Veyra.Query.Home;

/// <summary>
/// The expected load of the coming hours, learned from the same hours on previous days.
/// </summary>
/// <param name="Hours">The coming hours, from the next one on.</param>
/// <param name="DaysOfHistory">How many days of records the expectation is learned from.</param>
internal sealed record ExpectedLoad(IReadOnlyList<ExpectedHourResponse> Hours, int DaysOfHistory)
{
    // An hour seen once could be a fluke; twice is the least that shows a habit.
    private const int _minimumSamples = 2;

    /// <summary>
    /// Learns the expected load from hourly record counts.
    /// </summary>
    /// <remarks>
    /// History starts at the first hour with records, so the hours before Veyra received anything do not count as
    /// quiet. Hours of the day are compared in UTC, which matches local hours in every zone with a whole-hour offset.
    /// </remarks>
    /// <param name="history">Hourly buckets before <paramref name="hour"/>, oldest first.</param>
    /// <param name="hour">The start of the current hour.</param>
    /// <param name="hoursAhead">How many coming hours to expect.</param>
    /// <returns>The expectation.</returns>
    public static ExpectedLoad Calculate(IReadOnlyList<LogBucket> history, DateTimeOffset hour, int hoursAhead)
    {
        ArgumentNullException.ThrowIfNull(history);

        var relevant = history.SkipWhile(bucket => bucket.Total == 0).ToList();
        var hours = new List<ExpectedHourResponse>(hoursAhead);

        if (relevant.Count == 0)
        {
            for (var ahead = 1; ahead <= hoursAhead; ahead++)
            {
                hours.Add(new ExpectedHourResponse(hour.AddHours(ahead), null));
            }

            return new ExpectedLoad(hours, 0);
        }

        var average = relevant.Average(bucket => (double)bucket.Total);
        var days = (int)Math.Ceiling((hour - relevant[0].Start).TotalHours / 24);

        for (var ahead = 1; ahead <= hoursAhead; ahead++)
        {
            var start = hour.AddHours(ahead);
            var samples = relevant.Where(bucket => bucket.Start.UtcDateTime.Hour == start.UtcDateTime.Hour).ToList();

            double? load = samples.Count >= _minimumSamples && average > 0
                ? samples.Average(bucket => (double)bucket.Total) / average
                : null;

            hours.Add(new ExpectedHourResponse(start, load));
        }

        return new ExpectedLoad(hours, days);
    }
}
