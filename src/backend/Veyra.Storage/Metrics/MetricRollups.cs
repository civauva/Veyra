using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Rolls metric points up into 5-minute and hourly rows (ADR 0015), so charts over days read a few rows per series.
/// </summary>
/// <remarks>
/// <para>
/// Every finished 5 minutes is rolled up from the points once it is <see cref="Grace"/> old, so points that arrive a
/// little late still make it; every finished hour is rolled up from its 5-minute rows. Reads take everything newer
/// from the points, so charts stay current to the minute.
/// </para>
/// <para>
/// The job and the metric writer meet at <c>storage.metric_rollup_state</c>: the writer holds a share lock on it while
/// it writes a batch, and the job takes it exclusively before it rolls up, so a batch is either in the rollup or
/// written after it, and then marks its hour as late. Late hours are rolled up again.
/// </para>
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="logger">Receives a note when the job has much to catch up, for example after Veyra was stopped.</param>
internal sealed partial class MetricRollups(NpgsqlDataSource dataSource, ILogger<MetricRollups> logger)
{
    /// <summary>How long after its end a bucket is rolled up.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan _fiveMinutes = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _hour = TimeSpan.FromHours(1);

    // One run never holds the writers back for long: at most an hour of points or a day of 5-minute rows at a time.
    private static readonly TimeSpan _fiveMinuteStep = TimeSpan.FromHours(1);
    private static readonly TimeSpan _hourStep = TimeSpan.FromDays(1);

    private const string _fromPoints =
        """
        INSERT INTO storage.metric_rollup_5m
        SELECT DISTINCT ON (series_id, bin)
               series_id, bin, count(*) OVER whole, count(value) OVER whole, sum(value) OVER whole,
               min(value) OVER whole, max(value) OVER whole,
               timestamp, start_timestamp, value, count, sum, min, max, bounds, bucket_counts
        FROM (SELECT point.*, date_bin('5 minutes', timestamp, timestamptz '1970-01-01 00:00:00+00') AS bin
              FROM storage.metric_point AS point
              WHERE timestamp >= $1 AND timestamp < $2) AS binned
        WINDOW whole AS (PARTITION BY series_id, bin)
        ORDER BY series_id, bin, timestamp DESC
        """;

    private const string _fromFiveMinutes =
        """
        INSERT INTO storage.metric_rollup_1h
        SELECT DISTINCT ON (series_id, bin)
               series_id, bin, sum(points) OVER whole, sum(value_count) OVER whole, sum(value_sum) OVER whole,
               min(value_min) OVER whole, max(value_max) OVER whole,
               last_timestamp, last_start, last_value, last_count, last_sum, last_min, last_max, last_bounds,
               last_bucket_counts
        FROM (SELECT rollup.*, date_bin('1 hour', bucket, timestamptz '1970-01-01 00:00:00+00') AS bin
              FROM storage.metric_rollup_5m AS rollup
              WHERE bucket >= $1 AND bucket < $2) AS binned
        WINDOW whole AS (PARTITION BY series_id, bin)
        ORDER BY series_id, bin, last_timestamp DESC
        """;

    private const string _deleteFiveMinutes =
        "DELETE FROM storage.metric_rollup_5m WHERE bucket >= $1 AND bucket < $2";

    private const string _deleteHours = "DELETE FROM storage.metric_rollup_1h WHERE bucket >= $1 AND bucket < $2";

    /// <summary>Rolls up everything finished by now, then the hours that received late points.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the run; what is committed stays.</param>
    /// <returns>A task that completes when the rollups have caught up.</returns>
    public async Task RollUpAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var finished = TimeBins.Floor(now - Grace, _fiveMinutes);

        while (await RollUpFiveMinutesAsync(finished, cancellationToken))
        {
        }

        while (await RollUpHoursAsync(cancellationToken))
        {
        }

        await RollUpLateHoursAsync(cancellationToken);
    }

    /// <summary>
    /// Rolls up the newest day of points written before rollups existed, up to where the job took over, and takes it
    /// off <c>storage.metric_rollup_backfill</c> in the same transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work; the day stays to do.</param>
    /// <returns><see langword="false"/> when no day was left.</returns>
    public async Task<bool> BackfillNextDayAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        DateOnly day;
        DateTimeOffset upTo;

        await using (var next = new NpgsqlCommand(
            "SELECT day, up_to FROM storage.metric_rollup_backfill ORDER BY day DESC LIMIT 1 FOR UPDATE",
            connection))
        {
            await using var reader = await next.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return false;
            }

            day = reader.GetFieldValue<DateOnly>(0);
            upTo = reader.GetFieldValue<DateTimeOffset>(1);
        }

        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = Min(from.AddDays(1), upTo);

        // Late points may have given some of the day rows already; they are rolled up again with the rest.
        if (from < to)
        {
            await ExecuteAsync(connection, _deleteFiveMinutes, from, to, cancellationToken);
            await ExecuteAsync(connection, _fromPoints, from, to, cancellationToken);
            await ExecuteAsync(connection, _deleteHours, from, to, cancellationToken);
            await ExecuteAsync(connection, _fromFiveMinutes, from, to, cancellationToken);
        }

        await using (var done = new NpgsqlCommand(
            "DELETE FROM storage.metric_rollup_backfill WHERE day = $1",
            connection))
        {
            done.Parameters.AddWithValue(day);
            await done.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Rolls up the next step of finished 5 minutes; returns whether there was anything to do.</summary>
    private async Task<bool> RollUpFiveMinutesAsync(DateTimeOffset finished, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Waits for every batch being written to commit; batches that start now wait for this transaction.
        var (from, _) = await LockStateAsync(connection, cancellationToken);
        var to = Min(finished, from + _fiveMinuteStep);

        if (to <= from)
        {
            return false;
        }

        if (finished - from > _fiveMinuteStep)
        {
            LogCatchingUp(from, finished - from);
        }

        await ExecuteAsync(connection, _fromPoints, from, to, cancellationToken);
        await SetStateAsync(connection, "five_minutes", to, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Rolls up the next step of finished hours from their 5-minute rows.</summary>
    private async Task<bool> RollUpHoursAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var (fiveMinutes, from) = await LockStateAsync(connection, cancellationToken);
        var to = Min(TimeBins.Floor(fiveMinutes, _hour), from + _hourStep);

        if (to <= from)
        {
            return false;
        }

        await ExecuteAsync(connection, _fromFiveMinutes, from, to, cancellationToken);
        await SetStateAsync(connection, "hours", to, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Rolls up again every hour that received points after it was rolled up.</summary>
    private async Task RollUpLateHoursAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var late = new List<(DateTimeOffset Hour, long Version)>();
        DateTimeOffset fiveMinutes;
        DateTimeOffset hours;

        await using (var command = new NpgsqlCommand(
            """
            SELECT hour, version, state.five_minutes, state.hours
            FROM storage.metric_rollup_dirty, storage.metric_rollup_state AS state
            ORDER BY hour
            """,
            connection))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            fiveMinutes = hours = DateTimeOffset.MinValue;

            while (await reader.ReadAsync(cancellationToken))
            {
                late.Add((reader.GetFieldValue<DateTimeOffset>(0), reader.GetInt64(1)));
                fiveMinutes = reader.GetFieldValue<DateTimeOffset>(2);
                hours = reader.GetFieldValue<DateTimeOffset>(3);
            }
        }

        foreach (var (hour, version) in late)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var end = Min(hour + _hour, fiveMinutes);

            if (hour < end)
            {
                await ExecuteAsync(connection, _deleteFiveMinutes, hour, end, cancellationToken);
                await ExecuteAsync(connection, _fromPoints, hour, end, cancellationToken);
            }

            if (hour + _hour <= hours)
            {
                await ExecuteAsync(connection, _deleteHours, hour, hour + _hour, cancellationToken);
                await ExecuteAsync(connection, _fromFiveMinutes, hour, hour + _hour, cancellationToken);
            }

            // A point that arrived while this ran gave the hour a new version, so the hour stays marked for next time.
            await using (var done = new NpgsqlCommand(
                "DELETE FROM storage.metric_rollup_dirty WHERE hour = $1 AND version = $2",
                connection))
            {
                done.Parameters.AddWithValue(hour);
                done.Parameters.AddWithValue(version);
                await done.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task<(DateTimeOffset FiveMinutes, DateTimeOffset Hours)> LockStateAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT five_minutes, hours FROM storage.metric_rollup_state FOR UPDATE",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return (reader.GetFieldValue<DateTimeOffset>(0), reader.GetFieldValue<DateTimeOffset>(1));
    }

    private static async Task SetStateAsync(
        NpgsqlConnection connection,
        string column,
        DateTimeOffset value,
        CancellationToken cancellationToken)
    {
        // The column is one of two constants of this class.
        await using var command = new NpgsqlCommand(
            $"UPDATE storage.metric_rollup_state SET {column} = $1",
            connection);
        command.Parameters.AddWithValue(value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Rolling up metrics from {From}, {Behind} behind; charts read the points until it catches up")]
    private partial void LogCatchingUp(DateTimeOffset from, TimeSpan behind);
}
