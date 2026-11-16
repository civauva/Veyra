using Npgsql;

namespace Veyra.Storage.Logs;

/// <summary>
/// Counts the records written before counting existed (ADR 0015), one day at a time and newest first, because the
/// newest days are the ones people look at.
/// </summary>
/// <remarks>
/// Each day is counted in one transaction together with taking it off <c>storage.log_count_backfill</c>, so a day is
/// either counted once or not at all, even if Veyra stops half way. Only records up to the day's <c>up_to_id</c> are
/// counted: the writer counted every later one.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class LogCountBackfill(NpgsqlDataSource dataSource)
{
    private const string _countDay =
        """
        WITH day AS (
            DELETE FROM storage.log_count_backfill
            WHERE day = (SELECT max(day) FROM storage.log_count_backfill)
            RETURNING day, up_to_id),
        minutes AS (
            SELECT date_bin('1 minute', timestamp, timestamptz '1970-01-01 00:00:00+00') AS minute,
                   environment, service_name, severity, block_kind, count(*) AS records
            FROM storage.log_record, day
            WHERE timestamp >= day.day::timestamp AT TIME ZONE 'UTC'
              AND timestamp < (day.day + 1)::timestamp AT TIME ZONE 'UTC'
              AND id <= day.up_to_id
            GROUP BY 1, 2, 3, 4, 5),
        minute_rows AS (
            INSERT INTO storage.log_count_minute AS count
                (minute, environment, service_name, severity, block_kind, records)
            SELECT minute, environment, service_name, severity, block_kind, records FROM minutes
            ON CONFLICT (minute, service_name, severity, environment, block_kind)
                DO UPDATE SET records = count.records + excluded.records)
        INSERT INTO storage.log_count_hour AS count (hour, environment, service_name, severity, block_kind, records)
        SELECT date_bin('1 hour', minute, timestamptz '1970-01-01 00:00:00+00'), environment, service_name, severity,
               block_kind, sum(records)
        FROM minutes
        GROUP BY 1, 2, 3, 4, 5
        ON CONFLICT (hour, service_name, severity, environment, block_kind)
            DO UPDATE SET records = count.records + excluded.records
        """;

    /// <summary>Counts the newest day still to do.</summary>
    /// <param name="cancellationToken">Cancels the work; the day stays to do.</param>
    /// <returns><see langword="false"/> when no day was left.</returns>
    public async Task<bool> CountNextDayAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var pending = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM storage.log_count_backfill)",
            connection))
        {
            if (!(bool)(await pending.ExecuteScalarAsync(cancellationToken))!)
            {
                return false;
            }
        }

        // One statement takes the day off the list and adds its counts, so both happen or neither does.
        await using (var count = new NpgsqlCommand(_countDay, connection))
        {
            await count.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
