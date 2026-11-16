using System.Globalization;

namespace Veyra.Query.Live;

/// <summary>
/// The small pieces Veyra's findings are written with (ADR 0020): lengths of time, counts, and lists, as people say
/// them.
/// </summary>
internal static class Words
{
    /// <summary>A length of time in words: "45 seconds", "3 minutes", "1 hour 12 minutes", "2 days 3 hours".</summary>
    /// <param name="seconds">The length in seconds.</param>
    /// <returns>The words.</returns>
    public static string Duration(double seconds)
    {
        var whole = (long)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero);

        if (whole < 120)
        {
            return Count(whole, "second");
        }

        if (whole < 3600)
        {
            return Count(whole / 60, "minute");
        }

        if (whole < 86_400)
        {
            var minutes = whole % 3600 / 60;

            return minutes == 0
                ? Count(whole / 3600, "hour")
                : $"{Count(whole / 3600, "hour")} {Count(minutes, "minute")}";
        }

        var hours = whole % 86_400 / 3600;

        return hours == 0 ? Count(whole / 86_400, "day") : $"{Count(whole / 86_400, "day")} {Count(hours, "hour")}";
    }

    /// <summary>A length of time as a clock: <c>0:41</c>, <c>2:14</c>, <c>1:02:14</c>.</summary>
    /// <param name="seconds">The length in seconds.</param>
    /// <returns>The clock.</returns>
    public static string Clock(double seconds)
    {
        var whole = (long)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero);
        var hours = whole / 3600;
        var minutes = whole % 3600 / 60;
        var rest = whole % 60;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{rest:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{rest:00}");
    }

    /// <summary>A count with its noun: "1 request", "3 requests".</summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The noun in the singular; the plural adds an s.</param>
    /// <returns>The words.</returns>
    public static string Count(long count, string noun) =>
        count == 1
            ? $"1 {noun}"
            : string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}s");

    /// <summary>A list as people write it: "a", "a and b", "a, b and c".</summary>
    /// <param name="items">The items.</param>
    /// <returns>The list.</returns>
    public static string List(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
        };
    }

    /// <summary>A program's name, or words for a program that gave none.</summary>
    /// <param name="program">The name it connected with.</param>
    /// <returns>The words.</returns>
    public static string Program(string program) =>
        string.IsNullOrWhiteSpace(program) ? "an unnamed program" : program;
}
