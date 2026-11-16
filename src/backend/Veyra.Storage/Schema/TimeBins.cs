namespace Veyra.Storage.Schema;

/// <summary>
/// Clock-aligned time bins, as PostgreSQL's <c>date_bin</c> with the Unix epoch as origin makes them. Counts and
/// rollups (ADR 0015) are stored in such bins, so a bucket boundary never falls inside one.
/// </summary>
internal static class TimeBins
{
    /// <summary>Rounds a time down to a multiple of a width since the Unix epoch.</summary>
    /// <param name="time">The time.</param>
    /// <param name="width">The width.</param>
    /// <returns>The start of the bin that holds the time, in UTC.</returns>
    public static DateTimeOffset Floor(DateTimeOffset time, TimeSpan width)
    {
        var sinceEpoch = (time - DateTimeOffset.UnixEpoch).Ticks;
        var remainder = ((sinceEpoch % width.Ticks) + width.Ticks) % width.Ticks;

        return DateTimeOffset.UnixEpoch.AddTicks(sinceEpoch - remainder);
    }

    /// <summary>Rounds a time up to a multiple of a width since the Unix epoch.</summary>
    /// <param name="time">The time.</param>
    /// <param name="width">The width.</param>
    /// <returns>The time itself when it is on a bin boundary; otherwise the start of the next bin.</returns>
    public static DateTimeOffset Ceiling(DateTimeOffset time, TimeSpan width)
    {
        var floor = Floor(time, width);

        return floor == time ? floor : floor + width;
    }

    /// <summary>Whether a width is a whole number of units, so bins of the unit fit into it exactly.</summary>
    /// <param name="width">The width.</param>
    /// <param name="unit">The unit.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool IsMultiple(TimeSpan width, TimeSpan unit) => width >= unit && width.Ticks % unit.Ticks == 0;
}
