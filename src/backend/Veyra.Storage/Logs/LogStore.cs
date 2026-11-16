using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Logs;

/// <summary>
/// Writes log records to <c>storage.log_record</c> and their counts, searches the records, and counts them.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class LogStore(NpgsqlDataSource dataSource) : ILogWriter, ILogSearch, ILogRates, IBlockEvents
{
    private const string _copyCommand =
        """
        COPY storage.log_record
            (timestamp, observed_at, severity, severity_text, body, service_name, environment, trace_id, span_id,
             attributes, resource_attributes, block_kind, host_name)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string _selectColumns =
        "id, timestamp, observed_at, severity, severity_text, body, service_name, environment, trace_id, span_id, "
        + "attributes::text, resource_attributes::text";

    /// <summary>
    /// Stores a batch with PostgreSQL's binary <c>COPY</c>, the fastest way to insert many rows, and adds it to the
    /// counts in the same transaction, so the counts never disagree with the records (ADR 0015).
    /// </summary>
    /// <param name="records">The records to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the batch is stored.</returns>
    public async Task WriteAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var writer = await connection.BeginBinaryImportAsync(_copyCommand, cancellationToken);

        foreach (var record in records)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(record.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync(record.ObservedTimestamp, NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync((short)record.Severity, NpgsqlDbType.Smallint, cancellationToken);
            await WriteNullableAsync(writer, record.SeverityText, cancellationToken);
            await writer.WriteAsync(record.Body, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync(record.ServiceName, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, record.Environment, cancellationToken);
            await WriteNullableAsync(writer, record.TraceId, cancellationToken);
            await WriteNullableAsync(writer, record.SpanId, cancellationToken);
            await writer.WriteAsync(JsonSerializer.Serialize(record.Attributes), NpgsqlDbType.Jsonb, cancellationToken);
            await writer.WriteAsync(
                JsonSerializer.Serialize(record.ResourceAttributes),
                NpgsqlDbType.Jsonb,
                cancellationToken);
            await writer.WriteAsync((short)record.BlockKind, NpgsqlDbType.Smallint, cancellationToken);
            await WriteNullableAsync(writer, record.HostName, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
        await writer.CloseAsync(cancellationToken);
        await LogCounts.AddAsync(connection, records, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Finds records with parameterised SQL, newest first, one page at a time.
    /// </summary>
    /// <remarks>
    /// A search for words walks the hours of the window newest first through the index of words by hour (ADR 0016), so
    /// its time does not grow with how long ago a word last appeared. Days whose index is not built yet are searched
    /// the old way, in their place in the order.
    /// </remarks>
    /// <param name="query">The filters and the page.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The page, with a cursor when more records exist.</returns>
    public async Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // One extra row tells whether another page exists, without a second query.
        var wanted = query.Limit + 1;
        var items = new List<StoredLogRecord>();

        if (string.IsNullOrWhiteSpace(query.Text))
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = CreateSearchCommand(query, query.From, query.To, wanted);
            command.Connection = connection;
            await ReadAsync(command, items, cancellationToken);
        }
        else if ((await LogWords.SplitAsync(dataSource, query.Text, cancellationToken)).Count > 0)
        {
            await SearchWordsAsync(query, wanted, items, cancellationToken);
        }

        if (items.Count <= query.Limit)
        {
            return new LogPage(items, null);
        }

        items.RemoveRange(query.Limit, items.Count - query.Limit);
        var last = items[^1];

        return new LogPage(items, new LogCursor(last.Record.Timestamp, last.Id));
    }

    /// <summary>
    /// Builds the statement that searches a part of a window for words, hour by hour, newest hour first. The plan
    /// tests explain it.
    /// </summary>
    /// <remarks>
    /// Each hour looks up only its own keys, <c>word@hour</c>, and sorts only its own matches; an hour without the
    /// words costs one index lookup. The statement stops at the hour that fills the page. The hours come in the order
    /// of the array, newest first, and each hour's rows are sorted, so the first rows are the newest.
    /// </remarks>
    /// <param name="query">The filters and the cursor; its text holds at least one word.</param>
    /// <param name="from">The start of the part, inclusive.</param>
    /// <param name="to">The end of the part, exclusive.</param>
    /// <param name="rows">The most rows to return.</param>
    /// <returns>The command, which the caller disposes.</returns>
    internal static NpgsqlCommand CreateWordSearchCommand(
        LogQuery query,
        DateTimeOffset from,
        DateTimeOffset to,
        int rows)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = new NpgsqlCommand();
        command.Parameters.AddWithValue(LogWords.HoursNewestFirst(from, to));
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        command.Parameters.AddWithValue(query.Text!);

        // storage.log_terms of the search text in an hour gives exactly the keys its words have in that hour.
        var sql = new StringBuilder(
            $"""
            SELECT record.*
            FROM unnest($1::bigint[]) AS hour (h)
            CROSS JOIN LATERAL (
                SELECT {_selectColumns}
                FROM storage.log_record
                WHERE timestamp >= greatest(to_timestamp(hour.h * 3600), $2)
                  AND timestamp < least(to_timestamp((hour.h + 1) * 3600), $3)
                  AND storage.log_terms(body, timestamp) @> storage.log_terms($4, to_timestamp(hour.h * 3600))
            """);

        AppendFilters(sql, command, query with { Text = null }, includeServices: true);
        AppendCursor(sql, command, query);

        command.Parameters.AddWithValue(rows);
        var limit = command.Parameters.Count;
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY timestamp DESC, id DESC LIMIT ${limit}) AS record LIMIT ${limit}");

        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>
    /// Builds the statement that searches a part of a window in one go, newest first through the time index.
    /// </summary>
    /// <param name="query">The filters and the cursor.</param>
    /// <param name="from">The start of the part, inclusive.</param>
    /// <param name="to">The end of the part, exclusive.</param>
    /// <param name="rows">The most rows to return.</param>
    /// <returns>The command, which the caller disposes.</returns>
    private static NpgsqlCommand CreateSearchCommand(LogQuery query, DateTimeOffset from, DateTimeOffset to, int rows)
    {
        var command = new NpgsqlCommand();
        var sql = new StringBuilder(
            $"SELECT {_selectColumns} FROM storage.log_record WHERE timestamp >= $1 AND timestamp < $2");

        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);

        AppendFilters(sql, command, query, includeServices: true);
        AppendCursor(sql, command, query);

        command.Parameters.AddWithValue(rows);
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY timestamp DESC, id DESC LIMIT ${command.Parameters.Count}");

        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>Appends the condition that keeps only records after the page's cursor, if it has one.</summary>
    private static void AppendCursor(StringBuilder sql, NpgsqlCommand command, LogQuery query)
    {
        if (query.After is { } after)
        {
            command.Parameters.AddWithValue(after.Timestamp);
            command.Parameters.AddWithValue(after.Id);
            var count = command.Parameters.Count;
            sql.Append(CultureInfo.InvariantCulture, $" AND (timestamp, id) < (${count - 1}, ${count})");
        }
    }

    private static async Task ReadAsync(
        NpgsqlCommand command,
        List<StoredLogRecord> items,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(Read(reader));
        }
    }

    /// <summary>Searches a window for words, part by part, newest part first, until the page is full.</summary>
    private async Task SearchWordsAsync(
        LogQuery query,
        int wanted,
        List<StoredLogRecord> items,
        CancellationToken cancellationToken)
    {
        // Nothing after the cursor is wanted, so the walk starts at its hour. A record at the cursor's instant with a
        // lower ID still comes after it, and timestamps have whole microseconds, so one tick later is enough.
        var end = query.After is { } after && after.Timestamp < query.To ? after.Timestamp.AddTicks(1) : query.To;
        var unindexed = await LogWords.UnindexedDaysAsync(dataSource, query.From, end, cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        foreach (var part in LogWords.SplitWindow(query.From, end, unindexed))
        {
            var rows = wanted - items.Count;

            if (rows <= 0)
            {
                break;
            }

            if (!part.Indexed)
            {
                await using var old = CreateSearchCommand(query, part.From, part.To, rows);
                old.Connection = connection;
                await ReadAsync(old, items, cancellationToken);
                continue;
            }

            // Only the index of words by hour bounds the walk. Without this, PostgreSQL may walk the time index and
            // compute every record's words instead, which is the cost ADR 0016 removes.
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var setting = new NpgsqlCommand(
                "SELECT set_config('enable_indexscan', 'off', true)",
                connection,
                transaction))
            {
                await setting.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var walk = CreateWordSearchCommand(query, part.From, part.To, rows);
            walk.Connection = connection;
            walk.Transaction = transaction;
            await ReadAsync(walk, items, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Counts matching records per bucket and per service in one query, from the counts wherever they can answer.
    /// </summary>
    /// <remarks>
    /// Buckets are aligned to the clock (see <see cref="LogCountPlan"/>): the first starts at the window's start and
    /// ends at the first round boundary.
    /// </remarks>
    /// <param name="query">The filters and the time window.</param>
    /// <param name="bucketSize">The width of each bucket.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>The summary, with every bucket of the window present.</returns>
    public async Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await LogCounts.CountAsync(dataSource, query, bucketSize, cancellationToken);
        var starts = LogCountPlan.BucketStarts(query.From, query.To, bucketSize);
        var totals = new long[starts.Count];
        var errors = new long[starts.Count];
        var services = query.Services is { Count: > 0 } chosen ? chosen.ToHashSet(StringComparer.Ordinal) : null;

        // The buckets honour the service filter; the service counts do not, so other services can still be added.
        // Records of other blocks, such as a database's query events, are in the buckets but never a service.
        foreach (var row in rows.Where(row => services is null || services.Contains(row.ServiceName)))
        {
            var index = LogCountPlan.BucketOf(row.Bucket!.Value, query.From, bucketSize);
            totals[index] += row.Total;
            errors[index] += row.Errors;
        }

        var buckets = starts.Select((start, index) => new LogBucket(start, totals[index], errors[index])).ToList();
        var perService = rows
            .Where(row => row.Kind == AlertBlockKind.Service)
            .GroupBy(row => row.ServiceName, StringComparer.Ordinal)
            .Select(group => new ServiceCount(group.Key, group.Sum(row => row.Total)))
            .Where(count => count.Count > 0)
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.Name, StringComparer.Ordinal)
            .Take(50)
            .ToList();

        return new LogSummary(bucketSize, buckets, perService);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
        LogQuery query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using var command = dataSource.CreateCommand();
        command.Parameters.AddWithValue(query.From);
        command.Parameters.AddWithValue(query.To);

        var sql = new StringBuilder(
            """
            SELECT body, count(*), min(timestamp), max(timestamp)
            FROM storage.log_record
            WHERE timestamp >= $1 AND timestamp < $2
            """);
        AppendFilters(sql, command, query, includeServices: true);

        command.Parameters.AddWithValue(limit);
        sql.Append(
            CultureInfo.InvariantCulture,
            $" GROUP BY body ORDER BY count(*) DESC, body LIMIT ${command.Parameters.Count}");
        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var groups = new List<LogMessageGroup>();

        while (await reader.ReadAsync(cancellationToken))
        {
            groups.Add(new LogMessageGroup(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return groups;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ServiceLogRate>> GetServiceRatesAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        // The alert rules call it every 30 seconds and the home every few seconds, so it reads the counts.
        var query = new LogQuery(windowStart, windowEnd, null, null, null, null, 0, null);
        var rows = await LogCounts.CountAsync(dataSource, query, null, cancellationToken);

        // Only services have an error rate: a database's query events are not its errors (ADR 0018).
        return rows
            .Where(row => row.Kind == AlertBlockKind.Service)
            .GroupBy(row => (row.Environment, row.ServiceName))
            .Select(group => new ServiceLogRate(
                group.Key.Environment,
                group.Key.ServiceName,
                group.Sum(row => row.Total),
                group.Sum(row => row.Errors)))
            .Where(rate => rate.Total > 0)
            .ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A block's records are found as a service's are, through the index of names and times: the name is the block's.
    /// </remarks>
    public async Task<IReadOnlyList<LogRecord>> ListAsync(
        TelemetryBlock block,
        string eventName,
        DateTimeOffset since,
        DateTimeOffset until,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);

        await using var command = CreateEventsCommand(block, eventName, since, until, limit);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        command.Connection = connection;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var records = new List<LogRecord>();

        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(Read(reader).Record with
            {
                BlockKind = block.Kind,
                HostName = reader.IsDBNull(12) ? null : reader.GetString(12),
            });
        }

        return records;
    }

    /// <summary>Builds the statement that lists a block's events. The plan tests explain it.</summary>
    /// <param name="block">The block.</param>
    /// <param name="eventName">The event name.</param>
    /// <param name="since">Only events at or after this time.</param>
    /// <param name="until">Only events before this time.</param>
    /// <param name="limit">The most events to return.</param>
    /// <returns>The command, without a connection, which the caller disposes.</returns>
    internal static NpgsqlCommand CreateEventsCommand(
        TelemetryBlock block,
        string eventName,
        DateTimeOffset since,
        DateTimeOffset until,
        int limit)
    {
        var command = new NpgsqlCommand(
            $"""
            SELECT {_selectColumns}, host_name
            FROM storage.log_record
            WHERE service_name = $1 AND block_kind = $2 AND timestamp >= $3 AND timestamp < $4 AND attributes @> $5
            ORDER BY timestamp DESC
            LIMIT $6
            """);
        command.Parameters.AddWithValue(block.Name);
        command.Parameters.AddWithValue((short)block.Kind);
        command.Parameters.AddWithValue(since);
        command.Parameters.AddWithValue(until);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["event.name"] = eventName }));
        command.Parameters.AddWithValue(limit);

        return command;
    }

    /// <summary>
    /// Appends the optional filters of a query as parameterised conditions. Shared by search and counting, so both
    /// always agree on what matches.
    /// </summary>
    /// <param name="sql">The statement so far, which ends in a <c>WHERE</c> clause.</param>
    /// <param name="command">The command that receives the parameters.</param>
    /// <param name="query">The filters.</param>
    /// <param name="includeServices">Whether to filter by service too.</param>
    /// <param name="byHour">
    /// The words of the text and the part of the window, to match them through the index of words by hour (ADR 0016);
    /// <see langword="null"/> matches the text the old way, for days whose index is not built yet.
    /// </param>
    internal static void AppendFilters(
        StringBuilder sql,
        NpgsqlCommand command,
        LogQuery query,
        bool includeServices,
        (IReadOnlyList<string> Words, DateTimeOffset From, DateTimeOffset To)? byHour = null)
    {
        if (query.MinimumSeverity is { } severity)
        {
            sql.Append(Condition(command, "severity >= {0}", (short)severity));
        }

        if (includeServices && query.Services is { Count: > 0 } services)
        {
            sql.Append(Condition(command, "service_name = ANY({0})", services.ToArray()));
        }

        if (query.Environments is { Count: > 0 } environments)
        {
            sql.Append(Condition(command, "environment = ANY({0})", environments.ToArray()));
        }

        if (query.Attributes is { Count: > 0 } attributes)
        {
            // Containment (@>) matches records that have all these pairs, and can use the jsonb_path_ops index.
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(attributes));
            sql.Append(CultureInfo.InvariantCulture, $" AND attributes @> ${command.Parameters.Count}");
        }

        if (!string.IsNullOrWhiteSpace(query.Text) && byHour is { } words)
        {
            // A record has a word when one of its keys is the word in any hour of the part; its keys are all of its own
            // hour. A text without words matches nothing, as plainto_tsquery does.
            if (words.Words.Count == 0)
            {
                sql.Append(" AND false");
            }

            foreach (var word in words.Words)
            {
                sql.Append(Condition(
                    command,
                    "storage.log_terms(body, timestamp) && {0}",
                    LogWords.Keys(word, words.From, words.To)));
            }
        }
        else if (!string.IsNullOrWhiteSpace(query.Text))
        {
            // The same expression as the GIN index, so PostgreSQL can use it. plainto_tsquery treats the input as plain
            // words joined by AND, so user input can never form query syntax.
            sql.Append(Condition(
                command,
                "to_tsvector('simple', body) @@ plainto_tsquery('simple', {0})",
                query.Text));
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            sql.Append(Condition(command, "trace_id = {0}", query.TraceId.ToLowerInvariant()));
        }
    }

    private static string Condition(NpgsqlCommand command, string template, object value)
    {
        command.Parameters.AddWithValue(value);

        return " AND " + string.Format(CultureInfo.InvariantCulture, template, $"${command.Parameters.Count}");
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken token) =>
        value is null ? writer.WriteNullAsync(token) : writer.WriteAsync(value, NpgsqlDbType.Text, token);

    private static StoredLogRecord Read(NpgsqlDataReader reader) => new(
        reader.GetInt64(0),
        new LogRecord(
            Timestamp: reader.GetFieldValue<DateTimeOffset>(1),
            ObservedTimestamp: reader.GetFieldValue<DateTimeOffset>(2),
            Severity: (LogSeverity)reader.GetInt16(3),
            SeverityText: reader.IsDBNull(4) ? null : reader.GetString(4),
            Body: reader.GetString(5),
            ServiceName: reader.GetString(6),
            Environment: reader.IsDBNull(7) ? null : reader.GetString(7),
            TraceId: reader.IsDBNull(8) ? null : reader.GetString(8),
            SpanId: reader.IsDBNull(9) ? null : reader.GetString(9),
            Attributes: Deserialize(reader.GetString(10)),
            ResourceAttributes: Deserialize(reader.GetString(11))));

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
}
