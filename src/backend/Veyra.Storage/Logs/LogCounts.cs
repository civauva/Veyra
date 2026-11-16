using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Logs;

/// <summary>
/// Keeps and reads the counts of log records per minute and per hour (ADR 0015).
/// </summary>
/// <remarks>
/// Counting from the counts takes the same time however many records there are: a day has at most 1,440 minute rows
/// and 24 hour rows per environment, service, and severity. The records are read only where the counts cannot answer:
/// the seconds at the edges of a window, filters the counts do not hold, and days the backfill has not reached.
/// </remarks>
internal static class LogCounts
{
    private const string _upsertMinutes =
        """
        INSERT INTO storage.log_count_minute AS count (minute, environment, service_name, severity, records, block_kind)
        SELECT * FROM unnest($1::timestamptz[], $2::text[], $3::text[], $4::smallint[], $5::bigint[], $6::smallint[])
        ON CONFLICT (minute, service_name, severity, environment, block_kind)
            DO UPDATE SET records = count.records + excluded.records
        """;

    private const string _upsertHours =
        """
        INSERT INTO storage.log_count_hour AS count (hour, environment, service_name, severity, records, block_kind)
        SELECT * FROM unnest($1::timestamptz[], $2::text[], $3::text[], $4::smallint[], $5::bigint[], $6::smallint[])
        ON CONFLICT (hour, service_name, severity, environment, block_kind)
            DO UPDATE SET records = count.records + excluded.records
        """;

    // Local to the transaction (the third argument), so the pooled connection goes back with the defaults.
    private const string _widenScan =
        """
        SELECT set_config('work_mem', '64MB', true),
               set_config('max_parallel_workers_per_gather', '4', true),
               set_config('parallel_setup_cost', '0', true),
               set_config('parallel_tuple_cost', '0.01', true)
        """;

    // Buckets are aligned to the clock, as LogCountPlan.Floor aligns them.
    private const string _epoch = "timestamptz '1970-01-01 00:00:00+00'";

    /// <summary>Adds a batch's records to the counts, in the transaction that stores them.</summary>
    /// <param name="connection">The connection that wrote the batch, inside its open transaction.</param>
    /// <param name="records">The batch.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the counts are added.</returns>
    public static async Task AddAsync(
        NpgsqlConnection connection,
        IReadOnlyList<LogRecord> records,
        CancellationToken cancellationToken)
    {
        await UpsertAsync(connection, _upsertMinutes, records, TimeSpan.FromMinutes(1), cancellationToken);
        await UpsertAsync(connection, _upsertHours, records, TimeSpan.FromHours(1), cancellationToken);
    }

    /// <summary>Counts the records of a window that match a query's filters, per environment and service.</summary>
    /// <param name="dataSource">The PostgreSQL connection pool.</param>
    /// <param name="query">The window and the filters, except the services: callers need every service.</param>
    /// <param name="bucketSize">The bucket width, or <see langword="null"/> for one total per service.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>One row per bucket, environment, and service, in no particular order.</returns>
    public static async Task<IReadOnlyList<LogCountRow>> CountAsync(
        NpgsqlDataSource dataSource,
        LogQuery query,
        TimeSpan? bucketSize,
        CancellationToken cancellationToken)
    {
        var countable = string.IsNullOrWhiteSpace(query.Text)
            && string.IsNullOrWhiteSpace(query.TraceId)
            && query.Attributes is not { Count: > 0 };
        var uncounted = countable ? await UncountedDaysAsync(dataSource, query, cancellationToken) : [];
        IReadOnlyList<string>? words = null;
        IReadOnlyCollection<DateOnly> unindexed = [];

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            words = await LogWords.SplitAsync(dataSource, query.Text, cancellationToken);
            unindexed = await LogWords.UnindexedDaysAsync(dataSource, query.From, query.To, cancellationToken);
        }

        // A word or an attribute is counted from every record of the window: often all pages of a day's partition.
        var scansRecords = !string.IsNullOrWhiteSpace(query.Text) || query.Attributes is { Count: > 0 };

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (scansRecords)
        {
            await WidenScanAsync(connection, transaction, cancellationToken);
        }

        await using var command = CreateCommand(query, bucketSize, countable, uncounted, words, unindexed);
        command.Connection = connection;
        command.Transaction = transaction;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<LogCountRow>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LogCountRow(
                reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(4),
                reader.GetInt64(5),
                (AlertBlockKind)reader.GetInt16(3)));
        }

        return rows;
    }

    /// <summary>
    /// Gives the counting statement of the open transaction more memory and parallel workers, until the transaction
    /// ends.
    /// </summary>
    /// <remarks>
    /// Counting a word or an attribute reads every matching record, and a common one is on most pages of a day. With
    /// the default 4 MB of work memory the bitmap of those pages turns lossy, so every record of a page is checked
    /// again, and the grouping spills to disk. Four workers read the pages side by side; the planner is told that
    /// starting them and passing their rows on is cheap, or it keeps to one process. Together they make a day's count
    /// more than twice as fast (ADR 0015). The settings are local to the transaction, so no other statement gets them.
    /// </remarks>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the counting statement runs in.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>A task that completes when the settings are in place.</returns>
    internal static async Task WidenScanAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(_widenScan, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Builds the one statement that counts a window, segment by segment. The plan tests explain it.</summary>
    /// <param name="query">The window and the filters, without the service filter.</param>
    /// <param name="bucketSize">The bucket width, or <see langword="null"/> for one total per service.</param>
    /// <param name="countable">Whether the counts hold the query's filters.</param>
    /// <param name="uncounted">The days of the window the backfill has not counted yet.</param>
    /// <param name="lexemes">
    /// The words of the query's text, from <see cref="LogWords.SplitAsync"/>, when it has text.
    /// </param>
    /// <param name="unindexed">The days of the window whose words are not indexed by hour yet (ADR 0016).</param>
    /// <returns>The command, which the caller disposes.</returns>
    internal static NpgsqlCommand CreateCommand(
        LogQuery query,
        TimeSpan? bucketSize,
        bool countable,
        IReadOnlyCollection<DateOnly> uncounted,
        IReadOnlyList<string>? lexemes = null,
        IReadOnlyCollection<DateOnly>? unindexed = null)
    {
        var segments = LogCountPlan.Split(query.From, query.To, bucketSize, countable, uncounted);

        if (lexemes is not null)
        {
            segments = LogCountPlan.SplitUnindexed(segments, unindexed ?? []);
        }

        var command = new NpgsqlCommand();
        var sql = new StringBuilder();

        command.Parameters.AddWithValue((short)LogSeverity.Error);

        if (bucketSize is { } size)
        {
            command.Parameters.AddWithValue(size);
        }

        foreach (var segment in segments)
        {
            if (sql.Length > 0)
            {
                sql.Append(" UNION ALL ");
            }

            AppendSegment(sql, command, query, segment, bucketSize is not null, lexemes);
        }

        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>Appends the count of one segment, grouped by bucket, environment, and service.</summary>
    private static void AppendSegment(
        StringBuilder sql,
        NpgsqlCommand command,
        LogQuery query,
        LogCountSegment segment,
        bool bucketed,
        IReadOnlyList<string>? lexemes)
    {
        var (table, time, total, errors) = segment.Source switch
        {
            LogCountSource.Minutes => (
                "storage.log_count_minute",
                "minute",
                "sum(records)::bigint",
                "coalesce(sum(records) FILTER (WHERE severity >= $1), 0)::bigint"),
            LogCountSource.Hours => (
                "storage.log_count_hour",
                "hour",
                "sum(records)::bigint",
                "coalesce(sum(records) FILTER (WHERE severity >= $1), 0)::bigint"),
            _ => ("storage.log_record", "timestamp", "count(*)", "count(*) FILTER (WHERE severity >= $1)"),
        };
        var bucket = bucketed ? $"date_bin($2, {time}, {_epoch})" : "NULL::timestamptz";

        command.Parameters.AddWithValue(segment.From);
        command.Parameters.AddWithValue(segment.To);
        var to = command.Parameters.Count;

        sql.Append(
            CultureInfo.InvariantCulture,
            $"""
            (SELECT {bucket} AS bucket, environment, service_name, block_kind, {total}, {errors}
            FROM {table}
            WHERE {time} >= ${to - 1} AND {time} < ${to}
            """);

        if (segment.Source == LogCountSource.Records)
        {
            var byHour = lexemes is null ? ((IReadOnlyList<string>, DateTimeOffset, DateTimeOffset)?)null
                : (lexemes, segment.From, segment.To);
            LogStore.AppendFilters(sql, command, query, includeServices: false, byHour);
        }
        else if (segment.Source == LogCountSource.UnindexedRecords)
        {
            LogStore.AppendFilters(sql, command, query, includeServices: false);
        }
        else
        {
            // The counts hold severity and environment, the only other filters a countable query has.
            if (query.MinimumSeverity is { } severity)
            {
                command.Parameters.AddWithValue((short)severity);
                sql.Append(CultureInfo.InvariantCulture, $" AND severity >= ${command.Parameters.Count}");
            }

            if (query.Environments is { Count: > 0 } environments)
            {
                command.Parameters.AddWithValue(environments.ToArray());
                sql.Append(CultureInfo.InvariantCulture, $" AND environment = ANY(${command.Parameters.Count})");
            }
        }

        sql.Append(" GROUP BY 1, 2, 3, 4)");
    }

    /// <summary>The days of the window the backfill has not counted yet; usually none.</summary>
    private static async Task<List<DateOnly>> UncountedDaysAsync(
        NpgsqlDataSource dataSource,
        LogQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT day FROM storage.log_count_backfill WHERE day >= $1 AND day <= $2");
        command.Parameters.AddWithValue(DateOnly.FromDateTime(query.From.UtcDateTime));
        command.Parameters.AddWithValue(DateOnly.FromDateTime(query.To.UtcDateTime));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var days = new List<DateOnly>();

        while (await reader.ReadAsync(cancellationToken))
        {
            days.Add(reader.GetFieldValue<DateOnly>(0));
        }

        return days;
    }

    /// <summary>Adds the batch's counts at one width, in a fixed key order so writers never deadlock.</summary>
    private static async Task UpsertAsync(
        NpgsqlConnection connection,
        string sql,
        IReadOnlyList<LogRecord> records,
        TimeSpan width,
        CancellationToken cancellationToken)
    {
        var counts = records
            .GroupBy(record => (
                Time: TimeBins.Floor(record.Timestamp, width),
                record.Environment,
                record.ServiceName,
                Severity: (short)record.Severity,
                Kind: (short)record.BlockKind))
            .Select(group => (group.Key, Records: (long)group.Count()))
            .OrderBy(count => count.Key.Time)
            .ThenBy(count => count.Key.ServiceName, StringComparer.Ordinal)
            .ThenBy(count => count.Key.Severity)
            .ThenBy(count => count.Key.Environment, StringComparer.Ordinal)
            .ThenBy(count => count.Key.Kind)
            .ToList();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(counts.Select(count => count.Key.Time).ToArray());
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = counts.Select(count => count.Key.Environment).ToArray(),
        });
        command.Parameters.AddWithValue(counts.Select(count => count.Key.ServiceName).ToArray());
        command.Parameters.AddWithValue(counts.Select(count => count.Key.Severity).ToArray());
        command.Parameters.AddWithValue(counts.Select(count => count.Records).ToArray());
        command.Parameters.AddWithValue(counts.Select(count => count.Key.Kind).ToArray());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
