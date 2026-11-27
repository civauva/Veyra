using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Storage.Ingest;
using Veyra.Storage.Jobs;

namespace Veyra.Storage.Traces;

/// <summary>
/// Writes spans to <c>storage.span</c> and reads them back as traces and as the trace list (ADR 0014).
/// </summary>
/// <remarks>
/// Every read is bounded in time, so PostgreSQL reads only the daily partitions it needs. The list is read from the
/// root-span indexes alone and pages by start time and span ID; what the other spans of a listed trace add (their
/// count, failures, and services) is counted for that page's traces only.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="options">The Storage settings, with the attributes that name a job (ADR 0027).</param>
internal sealed class SpanStore(NpgsqlDataSource dataSource, IOptionsMonitor<StorageOptions> options)
    : ISpanWriter, ITraceSearch
{
    /// <summary>The partitioned table.</summary>
    public const string Table = "span";

    /// <summary>The most traces the duration chart reads, newest first, whatever the window holds.</summary>
    public const int MaxDurationsRead = 20_000;

    // Spans of one trace can start before the root on a sender with a slower clock, or long after it in a job; this
    // much on either side of the page's roots finds them and still keeps the read to a day or two.
    private static readonly TimeSpan _traceMargin = TimeSpan.FromMinutes(30);

    private const string _copyCommand =
        """
        COPY storage.span
            (trace_id, span_id, parent_span_id, name, kind, start_time, duration_us, status, status_message,
             service_name, environment, host_name, attributes, resource_attributes, events, links,
             dropped_attributes, dropped_events, dropped_links)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string _spanColumns =
        "trace_id, span_id, parent_span_id, name, kind, start_time, duration_us, status, status_message, service_name, "
        + "environment, host_name, attributes::text, resource_attributes::text, events::text, links::text, "
        + "dropped_attributes, dropped_events, dropped_links";

    // Per trace and service: how many spans and how many failed.
    private const string _servicesOfTraces =
        """
        SELECT trace_id, service_name, count(*)::int, count(*) FILTER (WHERE status = 2)::int
        FROM storage.span
        WHERE trace_id = ANY($1) AND start_time >= $2 AND start_time < $3
        GROUP BY trace_id, service_name
        """;

    // The failed spans of the page's traces, at most 50 a trace, with the message of their first exception.
    private const string _failuresOfTraces =
        """
        SELECT trace_id, span_id, parent_span_id, service_name, name, status_message, message
        FROM (
            SELECT trace_id, span_id, parent_span_id, service_name, name, status_message, start_time,
                   (SELECT event -> 'attributes' ->> 'exception.message'
                    FROM jsonb_array_elements(events) AS event
                    WHERE event ->> 'name' = 'exception'
                    LIMIT 1) AS message,
                   row_number() OVER (PARTITION BY trace_id ORDER BY start_time) AS position
            FROM storage.span
            WHERE trace_id = ANY($1) AND start_time >= $2 AND start_time < $3 AND status = 2) AS failed
        WHERE position <= 50
        ORDER BY trace_id, start_time
        """;

    /// <summary>
    /// Stores a batch with binary <c>COPY</c>, the fastest way to insert many rows, and adds it to the ingest counts
    /// (ADR 0023) and its job runs (ADR 0027) in the same transaction, so neither ever disagrees with the spans.
    /// </summary>
    /// <param name="spans">The spans to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the batch is stored.</returns>
    public async Task WriteAsync(IReadOnlyList<Span> spans, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var writer = await connection.BeginBinaryImportAsync(_copyCommand, cancellationToken);

        foreach (var span in spans)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(Convert.FromHexString(span.TraceId), NpgsqlDbType.Bytea, cancellationToken);
            await writer.WriteAsync(Convert.FromHexString(span.SpanId), NpgsqlDbType.Bytea, cancellationToken);

            if (span.ParentSpanId is null)
            {
                await writer.WriteNullAsync(cancellationToken);
            }
            else
            {
                await writer.WriteAsync(
                    Convert.FromHexString(span.ParentSpanId),
                    NpgsqlDbType.Bytea,
                    cancellationToken);
            }

            await writer.WriteAsync(span.Name, NpgsqlDbType.Text, cancellationToken);
            await writer.WriteAsync((short)span.Kind, NpgsqlDbType.Smallint, cancellationToken);
            await writer.WriteAsync(span.Start, NpgsqlDbType.TimestampTz, cancellationToken);
            await writer.WriteAsync((long)span.Duration.TotalMicroseconds, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync((short)span.Status, NpgsqlDbType.Smallint, cancellationToken);
            await WriteNullableAsync(writer, span.StatusMessage, cancellationToken);
            await writer.WriteAsync(span.ServiceName, NpgsqlDbType.Text, cancellationToken);
            await WriteNullableAsync(writer, span.Environment, cancellationToken);
            await WriteNullableAsync(writer, span.HostName, cancellationToken);
            await writer.WriteAsync(JsonSerializer.Serialize(span.Attributes), NpgsqlDbType.Jsonb, cancellationToken);
            await writer.WriteAsync(
                JsonSerializer.Serialize(span.ResourceAttributes),
                NpgsqlDbType.Jsonb,
                cancellationToken);
            await writer.WriteAsync(
                JsonSerializer.Serialize(span.Events.Select(@event => new
                {
                    time = @event.Timestamp,
                    name = @event.Name,
                    attributes = @event.Attributes,
                })),
                NpgsqlDbType.Jsonb,
                cancellationToken);
            await writer.WriteAsync(
                JsonSerializer.Serialize(span.Links.Select(link => new
                {
                    traceId = link.TraceId,
                    spanId = link.SpanId,
                    attributes = link.Attributes,
                })),
                NpgsqlDbType.Jsonb,
                cancellationToken);
            await writer.WriteAsync(span.Dropped.Attributes, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(span.Dropped.Events, NpgsqlDbType.Integer, cancellationToken);
            await writer.WriteAsync(span.Dropped.Links, NpgsqlDbType.Integer, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
        await writer.CloseAsync(cancellationToken);

        // After the copy, so a run's steps among this batch's own spans are found with those stored before.
        await JobRuns.AddTracedAsync(
            connection,
            spans,
            options.CurrentValue.Jobs.NameAttributeList(),
            cancellationToken);
        await IngestCounts.AddAsync(
            connection,
            IngestSignal.Spans,
            spans.Select(span => (span.Start, span.Environment)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Span>> GetTraceAsync(
        string traceId,
        DateTimeOffset? around,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceId);

        var id = Convert.FromHexString(traceId);

        if (around is { } time)
        {
            // Its day and the days next to it: a trace that started just before midnight, or a long job.
            var day = new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero);
            var spans = await ReadTraceAsync(id, day.AddDays(-1), day.AddDays(2), cancellationToken);

            if (spans.Count > 0)
            {
                return spans;
            }
        }

        return await ReadTraceAsync(id, null, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TracePage> ListAsync(TraceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = CreateRootsCommand(
            query,
            "trace_id, span_id, name, service_name, environment, start_time, duration_us, status",
            query.Limit + 1,
            paged: true);
        var roots = new List<TraceSummary>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                roots.Add(new TraceSummary(
                    Hex(reader, 0),
                    Hex(reader, 1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    TimeSpan.FromMicroseconds(reader.GetInt64(6)),
                    reader.GetInt16(7) == (short)SpanStatus.Error,
                    0,
                    0,
                    null,
                    []));
            }
        }

        if (roots.Count <= query.Limit)
        {
            return new TracePage(await CountOthersAsync(roots, cancellationToken), null);
        }

        roots.RemoveRange(query.Limit, roots.Count - query.Limit);
        var last = roots[^1];
        var items = await CountOthersAsync(roots, cancellationToken);

        if (query.Order == TraceOrder.Worst)
        {
            return new TracePage(
                items,
                null,
                new TraceWorstCursor(last.Failed, last.Duration, last.TraceId, last.RootSpanId));
        }

        return new TracePage(items, new TraceCursor(last.Start, last.RootSpanId));
    }

    /// <inheritdoc />
    public async Task<TraceDurations> DurationsAsync(
        TraceQuery query,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = CreateRootsCommand(
            query,
            "trace_id, start_time, duration_us, status",
            MaxDurationsRead,
            paged: false);
        var read = new List<TraceDurationPoint>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                read.Add(new TraceDurationPoint(
                    Hex(reader, 0),
                    reader.GetFieldValue<DateTimeOffset>(1),
                    TimeSpan.FromMicroseconds(reader.GetInt64(2)),
                    reader.GetInt16(3) == (short)SpanStatus.Error));
            }
        }

        if (read.Count == 0)
        {
            return new TraceDurations([], 0, null);
        }

        // Nearest rank: the duration that 95 of 100 traces did not exceed.
        var sorted = read.Select(point => point.Duration).Order().ToList();
        var p95 = sorted[(int)Math.Ceiling(sorted.Count * 0.95) - 1];

        // Failed and slow traces are what people look for, so all of them are drawn; the rest is an even sample.
        var notable = read.Where(point => point.Failed || point.Duration >= p95).Take(maxPoints).ToList();
        var rest = read.Where(point => !point.Failed && point.Duration < p95).ToList();
        var room = Math.Min(maxPoints - notable.Count, rest.Count);
        var sample = room <= 0
            ? []
            : Enumerable.Range(0, room).Select(i => rest[(int)((long)i * rest.Count / room)]);

        return new TraceDurations(
            notable.Concat(sample).OrderBy(point => point.Start).ToList(),
            read.Count,
            p95,
            read.Count(point => point.Failed),
            read.Count(point => !point.Failed && point.Duration >= p95));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TraceServiceCount>> ServicesAsync(
        TraceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = CreateRootsCommand(
            query with { Services = null },
            "service_name, duration_us, status",
            MaxDurationsRead,
            paged: false);

        // Counted from the newest roots the chart reads, so the cost has the same bound however busy the window was.
        // percentile_disc is the nearest rank, as the chart's 95th percentile.
        command.CommandText =
            "SELECT service_name, count(*)::int, count(*) FILTER (WHERE status = 2)::int, "
            + "percentile_disc(0.95) WITHIN GROUP (ORDER BY duration_us) "
            + $"FROM ({command.CommandText}) AS newest GROUP BY 1 ORDER BY 2 DESC, 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var services = new List<TraceServiceCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            services.Add(new TraceServiceCount(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                TimeSpan.FromMicroseconds(reader.GetInt64(3))));
        }

        return services;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The percentiles come from the newest roots the chart reads, so the 95th percentile is the one the list's slow
    /// filter uses. The counts are of every root in the window and in the period before, read from the root-span
    /// indexes alone; the plan tests explain them.
    /// </remarks>
    public async Task<TraceFigures> FiguresAsync(TraceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var (p50, p95) = await PercentilesAsync(query, cancellationToken);
        var length = query.To - query.From;
        var hasPrevious = query.From.UtcTicks - DateTimeOffset.MinValue.UtcTicks >= length.Ticks;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var batch = connection.CreateBatch();
        await using var figures = CreateFiguresCommand(query, p95);
        batch.BatchCommands.Add(Batched(figures));

        if (hasPrevious)
        {
            await using var previous = CreateFiguresCommand(
                query with { From = query.From - length, To = query.From },
                null);
            batch.BatchCommands.Add(Batched(previous));
        }

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var traces = reader.GetInt32(0);
        var failed = reader.GetInt32(1);
        var slow = reader.GetInt32(2);
        var services = reader.GetInt32(3);
        int? previousTraces = null;

        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
        {
            previousTraces = reader.GetInt32(0);
        }

        return new TraceFigures(traces, failed, slow, p50, p95, services, previousTraces);
    }

    /// <summary>
    /// Builds the statement that counts the roots of a window: all, failed, at least as slow as a duration, and their
    /// services. The plan tests explain it.
    /// </summary>
    /// <param name="query">The window and the filters.</param>
    /// <param name="slowFrom">The duration a slow trace takes at least; none counts as slow without it.</param>
    /// <returns>The command, without a connection, which the caller disposes.</returns>
    internal NpgsqlCommand CreateFiguresCommand(TraceQuery query, TimeSpan? slowFrom)
    {
        var command = dataSource.CreateCommand();
        var filters = RootFilters(command, query);
        command.Parameters.AddWithValue(slowFrom is { } duration ? (long)duration.TotalMicroseconds : long.MaxValue);
        command.CommandText =
            "SELECT count(*)::int, count(*) FILTER (WHERE status = 2)::int, "
            + string.Create(
                CultureInfo.InvariantCulture,
                $"count(*) FILTER (WHERE duration_us >= ${command.Parameters.Count})::int, ")
            + "count(DISTINCT service_name)::int "
            + $"FROM storage.span WHERE {filters}";

        return command;
    }

    // The median and the 95th percentile of the newest roots the chart reads, by nearest rank as the chart has them.
    private async Task<(TimeSpan? P50, TimeSpan? P95)> PercentilesAsync(
        TraceQuery query,
        CancellationToken cancellationToken)
    {
        await using var command = CreateRootsCommand(query, "duration_us", MaxDurationsRead, paged: false);
        command.CommandText =
            "SELECT percentile_disc(0.5) WITHIN GROUP (ORDER BY duration_us), "
            + "percentile_disc(0.95) WITHIN GROUP (ORDER BY duration_us) "
            + $"FROM ({command.CommandText}) AS newest";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return reader.IsDBNull(0)
            ? (null, null)
            : (TimeSpan.FromMicroseconds(reader.GetInt64(0)), TimeSpan.FromMicroseconds(reader.GetInt64(1)));
    }

    // A batch takes its own commands; the statement and its parameters are copied from one built for plan tests.
    private static NpgsqlBatchCommand Batched(NpgsqlCommand command)
    {
        var batched = new NpgsqlBatchCommand(command.CommandText);

        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            batched.Parameters.Add(parameter.Clone());
        }

        return batched;
    }

    /// <summary>
    /// Builds the statement over root spans: the window, the filters, and the order. The plan tests explain it.
    /// </summary>
    /// <param name="query">The window, filters, order, and cursor.</param>
    /// <param name="columns">The columns to read; all are in the root-span indexes.</param>
    /// <param name="limit">The most rows.</param>
    /// <param name="paged">
    /// Whether this is a page of the list, in the query's order after its cursor. Otherwise the newest roots are read,
    /// the bound the chart, the services, and the figures share.
    /// </param>
    /// <returns>The command, without a connection, which the caller disposes.</returns>
    internal NpgsqlCommand CreateRootsCommand(TraceQuery query, string columns, int limit, bool paged)
    {
        var command = dataSource.CreateCommand();
        var sql = new StringBuilder($"SELECT {columns} FROM storage.span WHERE {RootFilters(command, query)}");
        var worst = paged && query.Order == TraceOrder.Worst;

        if (worst && query.AfterWorst is { } afterWorst)
        {
            // All four keys descend, so one row comparison continues the order exactly, and the index can seek to it.
            command.Parameters.AddWithValue(afterWorst.Failed);
            command.Parameters.AddWithValue((long)afterWorst.Duration.TotalMicroseconds);
            command.Parameters.AddWithValue(Convert.FromHexString(afterWorst.TraceId));
            command.Parameters.AddWithValue(Convert.FromHexString(afterWorst.SpanId));
            var count = command.Parameters.Count;
            sql.Append(" AND ((status = 2), duration_us, trace_id, span_id) < ")
                .Append(CultureInfo.InvariantCulture, $"(${count - 3}, ${count - 2}, ${count - 1}, ${count})");
        }
        else if (paged && !worst && query.After is { } after)
        {
            command.Parameters.AddWithValue(after.Start);
            command.Parameters.AddWithValue(Convert.FromHexString(after.SpanId));
            var count = command.Parameters.Count;
            sql.Append(CultureInfo.InvariantCulture, $" AND (start_time, span_id) < (${count - 1}, ${count})");
        }

        // The worst order is spelled as the worst-first index is made, so the index reads the roots in order.
        sql.Append(
            worst
                ? " ORDER BY (status = 2) DESC, duration_us DESC, trace_id DESC, span_id DESC"
                : " ORDER BY start_time DESC, span_id DESC");
        command.Parameters.AddWithValue(limit);
        sql.Append(CultureInfo.InvariantCulture, $" LIMIT ${command.Parameters.Count}");
        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>
    /// Adds the conditions on root spans of a query's window and filters to a command's parameters.
    /// </summary>
    /// <param name="command">The command the parameters are added to, numbered from its first.</param>
    /// <param name="query">The window and the filters.</param>
    /// <returns>The conditions, for a <c>WHERE</c> clause.</returns>
    private static string RootFilters(NpgsqlCommand command, TraceQuery query)
    {
        var sql = new StringBuilder("parent_span_id IS NULL AND start_time >= $1 AND start_time < $2");

        command.Parameters.AddWithValue(query.From);
        command.Parameters.AddWithValue(query.To);

        if (query.Services is { Count: > 0 } services)
        {
            sql.Append(Condition(command, "service_name = ANY({0})", services.ToArray()));
        }

        if (query.Environments is { Count: > 0 } environments)
        {
            sql.Append(Condition(command, "environment = ANY({0})", environments.ToArray()));
        }

        if (query.FailedOnly)
        {
            sql.Append(" AND status = 2");
        }

        if (query.MinDuration is { } min)
        {
            sql.Append(Condition(command, "duration_us >= {0}", (long)min.TotalMicroseconds));
        }

        if (query.MaxDuration is { } max)
        {
            sql.Append(Condition(command, "duration_us <= {0}", (long)max.TotalMicroseconds));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            // The text is a parameter, and its LIKE wildcards are escaped, so it only ever matches itself.
            sql.Append(Condition(command, "name ILIKE '%' || {0} || '%'", EscapeLike(query.Name.Trim())));
        }

        if (query.Attributes is { Count: > 0 } attributes)
        {
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(attributes));
            sql.Append(CultureInfo.InvariantCulture, $" AND attributes @> ${command.Parameters.Count}");
        }

        return sql.ToString();
    }

    /// <summary>Adds each listed trace's span count, failures, cause, and services, in one round trip.</summary>
    private async Task<List<TraceSummary>> CountOthersAsync(List<TraceSummary> roots, CancellationToken token)
    {
        if (roots.Count == 0)
        {
            return roots;
        }

        var ids = roots.Select(root => Convert.FromHexString(root.TraceId)).ToArray();
        var from = roots.Min(root => root.Start) - _traceMargin;
        var to = roots.Max(root => root.Start + root.Duration) + _traceMargin;
        var services = new Dictionary<string, List<(string Service, int Spans, int Failed)>>(StringComparer.Ordinal);
        var failures = new Dictionary<string, List<FailedSpan>>(StringComparer.Ordinal);

        await using var batch = dataSource.CreateBatch();
        batch.BatchCommands.Add(Command(_servicesOfTraces, ids, from, to));
        batch.BatchCommands.Add(Command(_failuresOfTraces, ids, from, to));

        await using (var reader = await batch.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var traceId = Hex(reader, 0);

                if (!services.TryGetValue(traceId, out var list))
                {
                    services[traceId] = list = [];
                }

                list.Add((reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
            }

            await reader.NextResultAsync(token);

            while (await reader.ReadAsync(token))
            {
                var traceId = Hex(reader, 0);

                if (!failures.TryGetValue(traceId, out var list))
                {
                    failures[traceId] = list = [];
                }

                list.Add(new FailedSpan(
                    Hex(reader, 1),
                    reader.IsDBNull(2) ? null : Hex(reader, 2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        return roots.Select(root =>
        {
            var shares = services.GetValueOrDefault(root.TraceId) ?? [];

            return root with
            {
                Spans = shares.Sum(share => share.Spans),
                FailedSpans = shares.Sum(share => share.Failed),
                Cause = Cause(failures.GetValueOrDefault(root.TraceId)),
                Services = shares
                    .OrderByDescending(share => share.Spans)
                    .ThenBy(share => share.Service, StringComparer.Ordinal)
                    .Select(share => new TraceServiceShare(share.Service, share.Spans))
                    .ToList(),
            };
        }).ToList();

        NpgsqlBatchCommand Command(string sql, byte[][] traceIds, DateTimeOffset start, DateTimeOffset end)
        {
            var command = new NpgsqlBatchCommand(sql);
            command.Parameters.AddWithValue(traceIds);
            command.Parameters.AddWithValue(start);
            command.Parameters.AddWithValue(end);

            return command;
        }
    }

    /// <summary>
    /// The deepest failure: a failed span none of whose children failed, so the cause rather than the requests that
    /// passed the failure on; the earliest such span when there are several. Spans with an exception come first,
    /// because an exception explains more than a bare status such as "timeout" on the call below it.
    /// </summary>
    private static TraceFailure? Cause(List<FailedSpan>? failed)
    {
        if (failed is not { Count: > 0 })
        {
            return null;
        }

        var explained = failed.Where(span => span.Exception is not null).ToList();
        var pool = explained.Count > 0 ? explained : failed;
        var parents = pool.Select(span => span.ParentId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var cause = pool.FirstOrDefault(span => !parents.Contains(span.SpanId)) ?? pool[0];

        return new TraceFailure(cause.Service, cause.Name, cause.Exception ?? cause.StatusMessage ?? cause.Name);
    }

    /// <summary>Builds the statement that reads every span of a trace. The plan tests explain it.</summary>
    /// <param name="traceId">The trace ID as bytes.</param>
    /// <param name="from">The start of the days to read, or <see langword="null"/> for every day kept.</param>
    /// <param name="to">The end of the days to read.</param>
    /// <returns>The command, which the caller disposes.</returns>
    internal NpgsqlCommand CreateTraceCommand(byte[] traceId, DateTimeOffset? from, DateTimeOffset? to)
    {
        var command = dataSource.CreateCommand(
            from is null
                ? $"SELECT {_spanColumns} FROM storage.span WHERE trace_id = $1 ORDER BY start_time, span_id"
                : $"SELECT {_spanColumns} FROM storage.span "
                    + "WHERE trace_id = $1 AND start_time >= $2 AND start_time < $3 ORDER BY start_time, span_id");
        command.Parameters.AddWithValue(traceId);

        if (from is not null)
        {
            command.Parameters.AddWithValue(from.Value);
            command.Parameters.AddWithValue(to!.Value);
        }

        return command;
    }

    private async Task<IReadOnlyList<Span>> ReadTraceAsync(
        byte[] traceId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        await using var command = CreateTraceCommand(traceId, from, to);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var spans = new List<Span>();

        while (await reader.ReadAsync(cancellationToken))
        {
            spans.Add(ReadSpan(reader));
        }

        return spans;
    }

    private static Span ReadSpan(NpgsqlDataReader reader) => new(
        Hex(reader, 0),
        Hex(reader, 1),
        reader.IsDBNull(2) ? null : Hex(reader, 2),
        reader.GetString(3),
        (SpanKind)reader.GetInt16(4),
        reader.GetFieldValue<DateTimeOffset>(5),
        TimeSpan.FromMicroseconds(reader.GetInt64(6)),
        (SpanStatus)reader.GetInt16(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        Attributes(reader.GetString(12)),
        Attributes(reader.GetString(13)),
        Events(reader.GetString(14)),
        Links(reader.GetString(15)),
        new SpanDropped(reader.GetInt32(16), reader.GetInt32(17), reader.GetInt32(18)));

    private static Dictionary<string, string> Attributes(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    private static List<SpanEvent> Events(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateArray()
            .Select(@event => new SpanEvent(
                @event.GetProperty("time").GetDateTimeOffset(),
                @event.GetProperty("name").GetString() ?? string.Empty,
                Attributes(@event.GetProperty("attributes").GetRawText())))
            .ToList();
    }

    private static List<SpanLink> Links(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.EnumerateArray()
            .Select(link => new SpanLink(
                link.GetProperty("traceId").GetString() ?? string.Empty,
                link.GetProperty("spanId").GetString() ?? string.Empty,
                Attributes(link.GetProperty("attributes").GetRawText())))
            .ToList();
    }

    private static string Hex(NpgsqlDataReader reader, int ordinal) =>
        Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(ordinal));

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string Condition(NpgsqlCommand command, string template, object value)
    {
        command.Parameters.AddWithValue(value);

        return " AND " + string.Format(CultureInfo.InvariantCulture, template, $"${command.Parameters.Count}");
    }

    private static Task WriteNullableAsync(NpgsqlBinaryImporter writer, string? value, CancellationToken token) =>
        value is null ? writer.WriteNullAsync(token) : writer.WriteAsync(value, NpgsqlDbType.Text, token);

    /// <summary>A failed span of a listed trace, for finding the cause.</summary>
    private sealed record FailedSpan(
        string SpanId,
        string? ParentId,
        string Service,
        string Name,
        string? Exception,
        string? StatusMessage);
}
