using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Telemetry;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Ingest;

/// <summary>
/// Keeps and reads how many log records, spans, and metric points were stored per minute and environment (ADR 0023).
/// </summary>
/// <remarks>
/// Each writer adds its batch in the transaction that stores it, as the log counts are added (ADR 0015), so the counts
/// never disagree with what was stored. A minute is the telemetry's own time, as the log counts and metric charts use
/// it. A day has at most 1,440 rows per signal and environment, so a window of 30 days is read in milliseconds.
/// </remarks>
internal static class IngestCounts
{
    /// <summary>The partitioned table in the <c>storage</c> schema.</summary>
    public const string Table = "ingest_count_minute";

    private const string _upsert =
        """
        INSERT INTO storage.ingest_count_minute AS total (minute, environment, signal, count)
        SELECT * FROM unnest($1::timestamptz[], $2::text[], $3::text[], $4::bigint[])
        ON CONFLICT (minute, signal, environment) DO UPDATE SET count = total.count + excluded.count
        """;

    // Steps are aligned to the clock, as the Query module's windows align their buckets.
    private const string _epoch = "timestamptz '1970-01-01 00:00:00+00'";

    private static readonly TimeSpan _minute = TimeSpan.FromMinutes(1);

    /// <summary>The name a signal has in the table.</summary>
    /// <param name="signal">The signal.</param>
    /// <returns><c>logs</c>, <c>spans</c>, or <c>points</c>.</returns>
    public static string NameOf(IngestSignal signal) => signal switch
    {
        IngestSignal.Spans => "spans",
        IngestSignal.Points => "points",
        _ => "logs",
    };

    /// <summary>Adds a batch to the counts, in the transaction that stores it.</summary>
    /// <param name="connection">The connection that wrote the batch, inside its open transaction.</param>
    /// <param name="signal">What the batch holds.</param>
    /// <param name="items">The time and environment of every record, span, or point stored.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the counts are added.</returns>
    public static async Task AddAsync(
        NpgsqlConnection connection,
        IngestSignal signal,
        IEnumerable<(DateTimeOffset At, string? Environment)> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        // A fixed key order, so two writers adding to the same minutes never deadlock.
        var counts = items
            .GroupBy(item => (Minute: TimeBins.Floor(item.At, _minute), item.Environment))
            .Select(group => (group.Key, Count: (long)group.Count()))
            .OrderBy(count => count.Key.Minute)
            .ThenBy(count => count.Key.Environment, StringComparer.Ordinal)
            .ToList();

        if (counts.Count == 0)
        {
            return;
        }

        var name = NameOf(signal);

        await using var command = new NpgsqlCommand(_upsert, connection);
        command.Parameters.AddWithValue(counts.Select(count => count.Key.Minute).ToArray());
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = counts.Select(count => count.Key.Environment).ToArray(),
        });
        command.Parameters.AddWithValue(counts.Select(_ => name).ToArray());
        command.Parameters.AddWithValue(counts.Select(count => count.Count).ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Builds the statement that adds up a window's minutes per step and signal. The plan tests explain it.
    /// </summary>
    /// <param name="environment">Only this environment; every one when null.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window; a minute it ends inside is read whole.</param>
    /// <param name="step">The width of a step.</param>
    /// <returns>The command, which the caller disposes.</returns>
    public static NpgsqlCommand CreateCommand(
        string? environment,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan step)
    {
        var command = new NpgsqlCommand();
        var sql = new StringBuilder(
            $"""
            SELECT date_bin($1, minute, {_epoch}) AS step, signal, sum(count)::bigint
            FROM storage.ingest_count_minute
            WHERE minute >= $2 AND minute < $3
            """);

        command.Parameters.AddWithValue(step);
        command.Parameters.AddWithValue(TimeBins.Floor(from, _minute));
        command.Parameters.AddWithValue(TimeBins.Ceiling(to, _minute));

        if (environment is not null)
        {
            command.Parameters.AddWithValue(environment);
            sql.Append(CultureInfo.InvariantCulture, $" AND environment = ${command.Parameters.Count}");
        }

        sql.Append(" GROUP BY 1, 2");
        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>Reads the signal of a row.</summary>
    /// <param name="name">The name in the table.</param>
    /// <returns>The signal.</returns>
    public static IngestSignal SignalOf(string name) => name switch
    {
        "spans" => IngestSignal.Spans,
        "points" => IngestSignal.Points,
        _ => IngestSignal.Logs,
    };
}
