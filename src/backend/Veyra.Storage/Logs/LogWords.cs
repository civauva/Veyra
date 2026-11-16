using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Logs;

/// <summary>
/// Searching by word through the index of words by hour (ADR 0016): the words of a search text, the hours of a window,
/// and the index keys of a word in those hours.
/// </summary>
/// <remarks>
/// A key is a word, <c>@</c>, and the UTC hour as hours since 1970, as <c>storage.log_terms</c> builds it in
/// <c>V0005__index_words_by_hour.sql</c>. The two must agree, which the word search tests prove.
/// </remarks>
internal static class LogWords
{
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan _day = TimeSpan.FromDays(1);

    /// <summary>
    /// The words of a search text, as <c>plainto_tsquery('simple', text)</c> takes them: lower case, punctuation gone.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL connection pool; its parser splits the words.</param>
    /// <param name="text">The search text.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The words; none when the text holds no word, which then matches no record.</returns>
    public static async Task<IReadOnlyList<string>> SplitAsync(
        NpgsqlDataSource dataSource,
        string text,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT tsvector_to_array(to_tsvector('simple', $1))");
        command.Parameters.AddWithValue(text);

        return (string[])(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>The days of a window whose partition has no index of words by hour yet; usually none.</summary>
    /// <param name="dataSource">The PostgreSQL connection pool.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The days.</returns>
    public static async Task<IReadOnlyCollection<DateOnly>> UnindexedDaysAsync(
        NpgsqlDataSource dataSource,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT day FROM storage.log_terms_backfill WHERE day >= $1 AND day <= $2");
        command.Parameters.AddWithValue(DateOnly.FromDateTime(from.UtcDateTime));
        command.Parameters.AddWithValue(DateOnly.FromDateTime(to.UtcDateTime));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var days = new List<DateOnly>();

        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return days;
    }

    /// <summary>The hours that hold part of a window, as hours since 1970, newest first.</summary>
    /// <param name="from">The start, inclusive.</param>
    /// <param name="to">The end, exclusive.</param>
    /// <returns>The hours.</returns>
    public static long[] HoursNewestFirst(DateTimeOffset from, DateTimeOffset to)
    {
        var first = HourOf(from);
        var last = HourOf(to.AddTicks(-1));
        var hours = new long[Math.Max(0, last - first + 1)];

        for (var index = 0; index < hours.Length; index++)
        {
            hours[index] = last - index;
        }

        return hours;
    }

    /// <summary>The index keys of a word in every hour of a window.</summary>
    /// <param name="word">One of the words from <see cref="SplitAsync"/>.</param>
    /// <param name="from">The start, inclusive.</param>
    /// <param name="to">The end, exclusive.</param>
    /// <returns>The keys, for example <c>deadlock@498452</c>.</returns>
    public static string[] Keys(string word, DateTimeOffset from, DateTimeOffset to) =>
        HoursNewestFirst(from, to).Select(hour => FormattableString.Invariant($"{word}@{hour}")).ToArray();

    /// <summary>
    /// Splits a window into its parts with and without the index of words by hour, newest part first.
    /// </summary>
    /// <param name="from">The start, inclusive.</param>
    /// <param name="to">The end, exclusive.</param>
    /// <param name="unindexed">The days without the index, from <see cref="UnindexedDaysAsync"/>.</param>
    /// <returns>The parts, each with whether it is indexed.</returns>
    public static IReadOnlyList<(DateTimeOffset From, DateTimeOffset To, bool Indexed)> SplitWindow(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyCollection<DateOnly> unindexed)
    {
        var parts = new List<(DateTimeOffset From, DateTimeOffset To, bool Indexed)>();

        for (var day = TimeBins.Floor(from, _day); day < to; day += _day)
        {
            var part = (
                From: day > from ? day : from,
                To: day + _day < to ? day + _day : to,
                Indexed: !unindexed.Contains(DateOnly.FromDateTime(day.UtcDateTime)));

            if (parts.Count > 0 && parts[^1].Indexed == part.Indexed)
            {
                parts[^1] = parts[^1] with { To = part.To };
            }
            else
            {
                parts.Add(part);
            }
        }

        parts.Reverse();

        return parts;
    }

    // Floored, as the index floors it: a record at 02:59:59.9 belongs to hour 2.
    private static long HourOf(DateTimeOffset time) => TimeBins.Floor(time, _hour).ToUnixTimeSeconds() / 3600;
}
