using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Logs;

/// <summary>
/// Writes log records to <c>storage.log_record</c>, searches them, and counts them per service.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class LogStore(NpgsqlDataSource dataSource) : ILogWriter, ILogSearch, ILogRates
{
    private const string _copyCommand =
        """
        COPY storage.log_record
            (timestamp, observed_at, severity, severity_text, body, service_name, environment, trace_id, span_id,
             attributes, resource_attributes)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string _selectColumns =
        "id, timestamp, observed_at, severity, severity_text, body, service_name, environment, trace_id, span_id, "
        + "attributes::text, resource_attributes::text";

    /// <summary>
    /// Stores a batch with PostgreSQL's binary <c>COPY</c>, the fastest way to insert many rows.
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
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Finds records with parameterised SQL, newest first, one page at a time.
    /// </summary>
    /// <param name="query">The filters and the page.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The page, with a cursor when more records exist.</returns>
    public async Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = dataSource.CreateCommand();
        var sql = new StringBuilder(
            $"SELECT {_selectColumns} FROM storage.log_record WHERE timestamp >= $1 AND timestamp < $2");

        command.Parameters.AddWithValue(query.From);
        command.Parameters.AddWithValue(query.To);

        AppendFilters(sql, command, query, includeServices: true);

        if (query.After is { } after)
        {
            command.Parameters.AddWithValue(after.Timestamp);
            command.Parameters.AddWithValue(after.Id);
            var count = command.Parameters.Count;
            sql.Append(CultureInfo.InvariantCulture, $" AND (timestamp, id) < (${count - 1}, ${count})");
        }

        // One extra row tells whether another page exists, without a second query.
        command.Parameters.AddWithValue(query.Limit + 1);
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY timestamp DESC, id DESC LIMIT ${command.Parameters.Count}");

        command.CommandText = sql.ToString();

        var items = new List<StoredLogRecord>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(Read(reader));
            }
        }

        if (items.Count <= query.Limit)
        {
            return new LogPage(items, null);
        }

        items.RemoveAt(items.Count - 1);
        var last = items[^1];

        return new LogPage(items, new LogCursor(last.Record.Timestamp, last.Id));
    }

    /// <summary>
    /// Counts matching records per bucket and per service, with two parameterised queries.
    /// </summary>
    /// <param name="query">The filters and the time window.</param>
    /// <param name="bucketSize">The width of each bucket.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>The summary, with every bucket of the window present.</returns>
    public async Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Keyed by bucket number, not by start time: PostgreSQL keeps microseconds, .NET keeps 100 ns ticks, so a
        // start time read back from the database may differ slightly from the one computed here.
        var counts = new Dictionary<long, (long Total, long Errors)>();

        await using (var command = dataSource.CreateCommand())
        {
            // date_bin aligns buckets to the start of the window, so the bars line up with the axis.
            command.Parameters.AddWithValue(query.From);
            command.Parameters.AddWithValue(query.To);
            command.Parameters.AddWithValue(bucketSize);
            command.Parameters.AddWithValue((short)LogSeverity.Error);

            var sql = new StringBuilder(
                """
                SELECT date_bin($3, timestamp, $1) AS bucket, count(*), count(*) FILTER (WHERE severity >= $4)
                FROM storage.log_record
                WHERE timestamp >= $1 AND timestamp < $2
                """);
            AppendFilters(sql, command, query, includeServices: true);
            sql.Append(" GROUP BY bucket");
            command.CommandText = sql.ToString();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var index = (long)Math.Round((reader.GetFieldValue<DateTimeOffset>(0) - query.From) / bucketSize);
                counts[index] = (reader.GetInt64(1), reader.GetInt64(2));
            }
        }

        var buckets = new List<LogBucket>();

        for (long index = 0; query.From + (bucketSize * index) < query.To; index++)
        {
            var (total, errors) = counts.GetValueOrDefault(index);
            buckets.Add(new LogBucket(query.From + (bucketSize * index), total, errors));
        }

        var services = new List<ServiceCount>();

        await using (var command = dataSource.CreateCommand())
        {
            command.Parameters.AddWithValue(query.From);
            command.Parameters.AddWithValue(query.To);

            var sql = new StringBuilder(
                "SELECT service_name, count(*) FROM storage.log_record WHERE timestamp >= $1 AND timestamp < $2");
            AppendFilters(sql, command, query, includeServices: false);
            sql.Append(" GROUP BY service_name ORDER BY count(*) DESC, service_name LIMIT 50");
            command.CommandText = sql.ToString();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                services.Add(new ServiceCount(reader.GetString(0), reader.GetInt64(1)));
            }
        }

        return new LogSummary(bucketSize, buckets, services);
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
        // Uses the time index; the alert rules call it every 30 seconds over a few minutes of records.
        await using var command = dataSource.CreateCommand(
            """
            SELECT environment, service_name, count(*), count(*) FILTER (WHERE severity >= $3)
            FROM storage.log_record
            WHERE timestamp >= $1 AND timestamp < $2
            GROUP BY environment, service_name
            """);
        command.Parameters.AddWithValue(windowStart);
        command.Parameters.AddWithValue(windowEnd);
        command.Parameters.AddWithValue((short)LogSeverity.Error);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rates = new List<ServiceLogRate>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rates.Add(new ServiceLogRate(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)));
        }

        return rates;
    }

    /// <summary>
    /// Appends the optional filters of a query as parameterised conditions. Shared by search and summary, so both
    /// always agree on what matches.
    /// </summary>
    private static void AppendFilters(StringBuilder sql, NpgsqlCommand command, LogQuery query, bool includeServices)
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

        if (!string.IsNullOrWhiteSpace(query.Text))
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
