using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;

namespace Veyra.Storage.Jobs;

/// <summary>
/// Recognises job runs in what the span writer and the activity writer store, and adds them to
/// <c>storage.job_run</c> in the transaction of their batch (ADR 0027), as the ingest counts are added (ADR 0015).
/// </summary>
/// <remarks>
/// <para>
/// A traced run's steps are collected when its root span is written, from the root's direct children already stored
/// and those of the same batch. The SDKs export a span when it ends, and a parent ends after its children, so the
/// children of a job's root arrive before it or with it: reading them then keeps the steps for the 30 days the run is
/// kept, while the spans themselves go after 7. A child that arrives after its root, from another process with a slower
/// exporter, is not among the steps.
/// </para>
/// <para>
/// A SQL Server Agent run is its job history row of step 0. Its steps stay in the job history, kept 90 days, and are
/// read from there when a run is shown.
/// </para>
/// </remarks>
internal static class JobRuns
{
    /// <summary>The partitioned table in the <c>storage</c> schema.</summary>
    public const string Table = "job_run";

    /// <summary>
    /// The most steps kept of one traced run, its first ones: a loop of many children is not a list of steps.
    /// </summary>
    public const int MaxSteps = 100;

    /// <summary>The kind of activity record a run or step of a SQL Server Agent job is (ADR 0020).</summary>
    public const string AgentKind = "sqlserver.job";

    /// <summary>The attribute that holds the run's ID in the OpenTelemetry CI/CD convention.</summary>
    public const string RunIdAttribute = "cicd.pipeline.run.id";

    // The documented attributes a run's records and retries are read from, first match first.
    private static readonly string[] _processed = ["records.processed", "job.records.processed"];

    private static readonly string[] _total = ["records.total", "job.records.total"];

    private static readonly string[] _retries = ["retry.count", "job.retry.count"];

    // A child may start a little before its root on a host with a slower clock, or long after it in a long job.
    private static readonly TimeSpan _stepMargin = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions _json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private const string _steps =
        """
        SELECT trace_id, parent_span_id, name, duration_us, status, message
        FROM (
            SELECT child.trace_id, child.parent_span_id, child.name, child.duration_us, child.status,
                   coalesce(nullif(child.status_message, ''),
                       (SELECT event -> 'attributes' ->> 'exception.message'
                        FROM jsonb_array_elements(child.events) AS event
                        WHERE event ->> 'name' = 'exception'
                        LIMIT 1)) AS message,
                   row_number() OVER (
                       PARTITION BY child.trace_id, child.parent_span_id
                       ORDER BY child.start_time, child.span_id) AS position
            FROM (
                -- A resent span is stored twice; it is one step.
                SELECT DISTINCT ON (span.trace_id, span.span_id) span.*
                FROM storage.span AS span
                JOIN unnest($1::bytea[], $2::bytea[]) AS root (trace_id, span_id)
                  ON span.trace_id = root.trace_id AND span.parent_span_id = root.span_id
                WHERE span.start_time >= $3 AND span.start_time <= $4) AS child) AS step
        WHERE position <= $5
        ORDER BY trace_id, parent_span_id, position
        """;

    private const string _upsert =
        """
        INSERT INTO storage.job_run AS run
            (source, environment, block_kind, block_name, job_name, run_id, started_at, duration_seconds, status, steps,
             records_processed, records_total, retries, trace_id, message)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb, $11, $12, $13, $14, $15)
        ON CONFLICT (source, environment, block_name, job_name, started_at, run_id) DO UPDATE SET
            duration_seconds = excluded.duration_seconds,
            status = excluded.status,
            steps = CASE WHEN jsonb_array_length(excluded.steps) > 0 THEN excluded.steps ELSE run.steps END,
            records_processed = coalesce(excluded.records_processed, run.records_processed),
            records_total = coalesce(excluded.records_total, run.records_total),
            retries = coalesce(excluded.retries, run.retries),
            message = coalesce(excluded.message, run.message)
        """;

    /// <summary>The name a source has in the table.</summary>
    /// <param name="source">The source.</param>
    /// <returns><c>trace</c> or <c>sqlagent</c>.</returns>
    public static string NameOf(JobSource source) => source == JobSource.SqlAgent ? "sqlagent" : "trace";

    /// <summary>Reads a source's name in the table.</summary>
    /// <param name="name"><c>trace</c> or <c>sqlagent</c>.</param>
    /// <returns>The source.</returns>
    public static JobSource SourceOf(string name) => name == "sqlagent" ? JobSource.SqlAgent : JobSource.Trace;

    /// <summary>The name a step's status has in the stored steps.</summary>
    /// <param name="status">The status, or null when it is not known.</param>
    /// <returns><c>succeeded</c>, <c>failed</c>, <c>running</c>, or <c>unknown</c>.</returns>
    public static string NameOf(JobRunStatus? status) => status switch
    {
        JobRunStatus.Succeeded => "succeeded",
        JobRunStatus.Failed => "failed",
        JobRunStatus.Running => "running",
        _ => "unknown",
    };

    /// <summary>Reads a step's status in the stored steps.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The status, or null for one Veyra does not know.</returns>
    public static JobRunStatus? StatusOf(string? name) => name switch
    {
        "succeeded" => JobRunStatus.Succeeded,
        "failed" => JobRunStatus.Failed,
        "running" => JobRunStatus.Running,
        _ => null,
    };

    /// <summary>
    /// The runs among a batch's spans: each root span that carries one of the job name attributes, without its steps.
    /// Pure; tested without a database.
    /// </summary>
    /// <param name="spans">The batch.</param>
    /// <param name="nameAttributes">The attributes that name a job, first match first.</param>
    /// <returns>Each run with the ID of its root span.</returns>
    public static IReadOnlyList<(JobRun Run, string RootSpanId)> Traced(
        IEnumerable<Span> spans,
        IReadOnlyList<string> nameAttributes)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(nameAttributes);

        if (nameAttributes.Count == 0)
        {
            return [];
        }

        var runs = new List<(JobRun, string)>();

        foreach (var span in spans.Where(span => span.ParentSpanId is null))
        {
            if (First(span.Attributes, nameAttributes) is not { } name)
            {
                continue;
            }

            var traceId = span.TraceId.ToLowerInvariant();
            var job = new JobKey(
                JobSource.Trace,
                span.Environment,
                new TelemetryBlock(AlertBlockKind.Service, span.ServiceName),
                name);
            var message = string.IsNullOrEmpty(span.StatusMessage)
                ? span.Events.FirstOrDefault(@event => @event.Name == "exception")?.Attributes
                    .GetValueOrDefault("exception.message")
                : span.StatusMessage;

            runs.Add((
                new JobRun(
                    job,
                    First(span.Attributes, [RunIdAttribute]) ?? traceId,
                    span.Start,
                    span.Duration.TotalSeconds,
                    span.Status == SpanStatus.Error ? JobRunStatus.Failed : JobRunStatus.Succeeded,
                    [],
                    Whole(span.Attributes, _processed),
                    Whole(span.Attributes, _total),
                    Whole(span.Attributes, _retries) is { } retries ? (int)Math.Clamp(retries, 0, int.MaxValue) : null,
                    traceId,
                    message),
                span.SpanId.ToLowerInvariant()));
        }

        return runs;
    }

    /// <summary>
    /// The run a SQL Server Agent job history row stands for, when it is the row of a whole run (step 0). Pure; tested
    /// without a database.
    /// </summary>
    /// <remarks>
    /// SQL Server Agent's <c>run_status</c> is 1 for a run that succeeded and 4 for one in progress. Every other value
    /// is a run that did not succeed and counts as failed: 0 failed, 3 cancelled, and 2, a retry, which the history
    /// gives a step and not a whole run. Retries are counted from the steps when a run is shown.
    /// </remarks>
    /// <param name="record">The activity record.</param>
    /// <returns>The run, or null for a step or another kind of record.</returns>
    public static JobRun? Agent(ActivityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Kind != AgentKind || record.Attributes.GetValueOrDefault("step_id") != "0")
        {
            return null;
        }

        var status = record.Attributes.GetValueOrDefault("run_status") switch
        {
            "1" => JobRunStatus.Succeeded,
            "4" => JobRunStatus.Running,
            _ => JobRunStatus.Failed,
        };
        var duration = double.TryParse(
            record.Attributes.GetValueOrDefault("duration"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var seconds) && double.IsFinite(seconds)
            ? seconds
            : 0;

        return new JobRun(
            new JobKey(
                JobSource.SqlAgent,
                record.Environment,
                new TelemetryBlock(AlertBlockKind.Database, record.Block),
                record.Attributes.GetValueOrDefault("job_name") ?? string.Empty),
            record.Key,
            record.SampledAt,
            status == JobRunStatus.Running ? null : duration,
            status,
            [],
            null,
            null,
            null,
            null,
            string.IsNullOrEmpty(record.Body) ? null : record.Body);
    }

    /// <summary>
    /// Adds the traced runs of a span batch with their steps, in the transaction that stored the batch, after the
    /// batch's spans are written so its own children are found.
    /// </summary>
    /// <param name="connection">The connection that wrote the batch, inside its open transaction.</param>
    /// <param name="spans">The batch.</param>
    /// <param name="nameAttributes">The attributes that name a job, first match first.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the runs are added.</returns>
    public static async Task AddTracedAsync(
        NpgsqlConnection connection,
        IReadOnlyList<Span> spans,
        IReadOnlyList<string> nameAttributes,
        CancellationToken cancellationToken)
    {
        var traced = Traced(spans, nameAttributes);

        if (traced.Count == 0)
        {
            return;
        }

        var steps = await ReadStepsAsync(connection, traced, cancellationToken);
        var runs = traced
            .Select(pair => pair.Run with
            {
                Steps = steps.GetValueOrDefault((pair.Run.TraceId!, pair.RootSpanId)) ?? [],
            })
            .ToList();

        await using var batch = new NpgsqlBatch(connection);

        foreach (var run in Ordered(runs))
        {
            batch.BatchCommands.Add(Upsert(run));
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The statement that adds a run, or updates it when it was added before.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The command, for a batch.</returns>
    public static NpgsqlBatchCommand Upsert(JobRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var command = new NpgsqlBatchCommand(_upsert);
        var job = run.Job;

        command.Parameters.AddWithValue(NameOf(job.Source));
        command.Parameters.Add(Nullable(job.Environment, NpgsqlDbType.Text));
        command.Parameters.AddWithValue(AlertSubject.KindNameOf(job.Block.Kind));
        command.Parameters.AddWithValue(job.Block.Name);
        command.Parameters.AddWithValue(job.Name);
        command.Parameters.AddWithValue(run.RunId);
        command.Parameters.AddWithValue(run.StartedAt);
        command.Parameters.Add(Nullable(run.DurationSeconds, NpgsqlDbType.Double));
        command.Parameters.AddWithValue((short)run.Status);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(
            run.Steps.Select(step => new StoredStep(
                step.Name,
                step.DurationSeconds,
                NameOf(step.Status),
                step.Message)),
            _json));
        command.Parameters.Add(Nullable(run.RecordsProcessed, NpgsqlDbType.Bigint));
        command.Parameters.Add(Nullable(run.RecordsTotal, NpgsqlDbType.Bigint));
        command.Parameters.Add(Nullable(run.Retries, NpgsqlDbType.Integer));
        command.Parameters.Add(Nullable(
            run.TraceId is null ? null : Convert.FromHexString(run.TraceId),
            NpgsqlDbType.Bytea));
        command.Parameters.Add(Nullable(run.Message, NpgsqlDbType.Text));

        return command;
    }

    /// <summary>Reads the steps a run keeps.</summary>
    /// <param name="json">The <c>steps</c> column.</param>
    /// <returns>The steps.</returns>
    public static IReadOnlyList<JobStep> StepsOf(string json) =>
        [.. (JsonSerializer.Deserialize<List<StoredStep>>(json, _json) ?? [])
            .Select(step => new JobStep(
                step.Name ?? string.Empty,
                step.DurationSeconds,
                StatusOf(step.Status),
                step.Message))];

    /// <summary>The runs in a fixed order, so two writers adding the same runs never deadlock.</summary>
    /// <param name="runs">The runs.</param>
    /// <returns>The runs, ordered.</returns>
    public static IEnumerable<JobRun> Ordered(IEnumerable<JobRun> runs) =>
        runs.OrderBy(run => run.Job.Source)
            .ThenBy(run => run.Job.Environment, StringComparer.Ordinal)
            .ThenBy(run => run.Job.Block.Name, StringComparer.Ordinal)
            .ThenBy(run => run.Job.Name, StringComparer.Ordinal)
            .ThenBy(run => run.StartedAt)
            .ThenBy(run => run.RunId, StringComparer.Ordinal);

    private static async Task<Dictionary<(string TraceId, string SpanId), List<JobStep>>> ReadStepsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<(JobRun Run, string RootSpanId)> traced,
        CancellationToken cancellationToken)
    {
        var steps = new Dictionary<(string, string), List<JobStep>>();

        await using var command = new NpgsqlCommand(_steps, connection);
        command.Parameters.AddWithValue(traced.Select(pair => Convert.FromHexString(pair.Run.TraceId!)).ToArray());
        command.Parameters.AddWithValue(traced.Select(pair => Convert.FromHexString(pair.RootSpanId)).ToArray());
        command.Parameters.AddWithValue(traced.Min(pair => pair.Run.StartedAt) - _stepMargin);
        command.Parameters.AddWithValue(
            traced.Max(pair => pair.Run.StartedAt + TimeSpan.FromSeconds(pair.Run.DurationSeconds ?? 0)) + _stepMargin);
        command.Parameters.AddWithValue(MaxSteps);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (
                Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(0)),
                Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(1)));

            if (!steps.TryGetValue(key, out var list))
            {
                steps[key] = list = [];
            }

            list.Add(new JobStep(
                reader.GetString(2),
                reader.GetInt64(3) / 1_000_000d,
                reader.GetInt16(4) == (short)SpanStatus.Error ? JobRunStatus.Failed : JobRunStatus.Succeeded,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return steps;
    }

    private static string? First(IReadOnlyDictionary<string, string> attributes, IEnumerable<string> names) =>
        names
            .Select(name => attributes.TryGetValue(name, out var value) ? value.Trim() : null)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

    // A count sent as text; a value that is not a whole number is left out, so one odd attribute never fails a batch.
    private static long? Whole(IReadOnlyDictionary<string, string> attributes, IEnumerable<string> names) =>
        First(attributes, names) is { } text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number)
        && number >= 0
        && number <= long.MaxValue
            ? (long)number
            : null;

    private static NpgsqlParameter Nullable(object? value, NpgsqlDbType type) =>
        new() { Value = value ?? DBNull.Value, NpgsqlDbType = type };

    // A step as the steps column keeps it.
    private sealed record StoredStep(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("duration_seconds")] double? DurationSeconds,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("message")] string? Message);
}
