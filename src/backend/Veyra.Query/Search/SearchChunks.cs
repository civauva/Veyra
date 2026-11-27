namespace Veyra.Query.Search;

/// <summary>
/// Splits a search window into parts, newest first, so a count that runs out of time still has the most recent hours
/// (ADR 0022).
/// </summary>
internal static class SearchChunks
{
    private static readonly TimeSpan _first = TimeSpan.FromHours(1);

    /// <summary>
    /// The parts of a window, newest first: an hour, an hour, then each part twice the one before, the last cut at the
    /// window's start. A day is six parts, 31 days eleven.
    /// </summary>
    /// <param name="from">The start of the window, inclusive.</param>
    /// <param name="to">The end of the window, exclusive.</param>
    /// <returns>The parts, which together cover the window exactly.</returns>
    public static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> NewestFirst(
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var end = to;
        var length = _first;
        var parts = 0;

        while (end > from)
        {
            var start = end - length > from ? end - length : from;

            yield return (start, end);

            end = start;
            parts++;

            // The first two parts are an hour each, so the last hour is read on its own and quickly.
            if (parts > 1)
            {
                length *= 2;
            }
        }
    }
}
