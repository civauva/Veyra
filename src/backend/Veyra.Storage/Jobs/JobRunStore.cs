using System.Globalization;
using System.Text;
using Npgsql;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;

namespace Veyra.Storage.Jobs;

/// <summary>Reads the job runs of <c>storage.job_run</c> (ADR 0027).</summary>
/// <remarks>
/// A window of every job reads the time index of its days and adds each job up in the database, so the answer grows
/// with the number of jobs, not with their runs. One job's runs are read through the key index, newest first, and
/// stop at their limit. The plan tests prove both.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class JobRunStore(NpgsqlDataSource dataSource) : IJobRuns
{
    private const string _runColumns =
        "run_id, started_at, duration_seconds, status, steps::text, records_processed, records_total, retries, "
        + "trace_id, message";

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobSummary>> SummarizeAsync(JobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateSummaryCommand(query);
        command.Connection = connection;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var summaries = new List<JobSummary>();

        while (await reader.ReadAsync(cancellationToken))
        {
            // The writers store only kinds they know; an unknown one would read as a service.
            var kind = AlertSubject.TryParseKind(reader.GetString(2), out var known) ? known : AlertBlockKind.Service;
            var job = new JobKey(
                JobRuns.SourceOf(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                new TelemetryBlock(kind, reader.GetString(3)),
                reader.GetString(4));
            var recent = reader.GetFieldValue<short[]>(11).Reverse().Select(status => (JobRunStatus)status).ToList();

            summaries.Add(new JobSummary(
                job,
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                new JobDuration(
                    reader.GetFieldValue<DateTimeOffset>(8),
                    Double(reader, 9),
                    (JobRunStatus)reader.GetInt16(10)),
                recent,
                Double(reader, 12),
                Double(reader, 13),
                Double(reader, 14),
                reader.IsDBNull(15) ? null : reader.GetInt64(15)));
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRun>> ListRunsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        bool failedOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateRunsCommand(job, periodStart, periodEnd, failedOnly, limit);
        command.Connection = connection;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var runs = new List<JobRun>();

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(new JobRun(
                job,
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                Double(reader, 2),
                (JobRunStatus)reader.GetInt16(3),
                JobRuns.StepsOf(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : Convert.ToHexStringLower(reader.GetFieldValue<byte[]>(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9))
            {
                TraceKept = reader.GetBoolean(10),
            });
        }

        return runs;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobDuration>> ListDurationsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = CreateJobCommand(
            "started_at, duration_seconds, status",
            job,
            periodStart,
            periodEnd,
            failedOnly: false,
            limit);
        command.Connection = connection;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var durations = new List<JobDuration>();

        while (await reader.ReadAsync(cancellationToken))
        {
            durations.Add(new JobDuration(
                reader.GetFieldValue<DateTimeOffset>(0),
                Double(reader, 1),
                (JobRunStatus)reader.GetInt16(2)));
        }

        return durations;
    }

    /// <summary>
    /// Builds the statement that adds up each job's runs of a window. The plan tests explain it.
    /// </summary>
    /// <param name="query">The window and which jobs.</param>
    /// <returns>The command, without a connection, which the caller disposes.</returns>
    public static NpgsqlCommand CreateSummaryCommand(JobQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Statuses: 0 succeeded, 1 failed, 2 running. Durations of runs still going on are not known yet.
        var command = new NpgsqlCommand();
        var sql = new StringBuilder(
            $"""
            SELECT source, environment, block_kind, block_name, job_name,
                   count(*)::int,
                   count(*) FILTER (WHERE status = 0)::int,
                   count(*) FILTER (WHERE status = 1)::int,
                   max(started_at),
                   (array_agg(duration_seconds ORDER BY started_at DESC))[1],
                   (array_agg(status ORDER BY started_at DESC))[1],
                   (array_agg(status ORDER BY started_at DESC))[1:{JobSummary.RecentRuns}],
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_seconds) FILTER (WHERE status = 0),
                   percentile_cont(0.5) WITHIN GROUP (ORDER BY duration_seconds) FILTER (WHERE status <> 2),
                   percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_seconds) FILTER (WHERE status <> 2),
                   sum(records_processed)::bigint
            FROM storage.job_run
            WHERE started_at >= $1 AND started_at < $2
            """);

        command.Parameters.AddWithValue(query.From);
        command.Parameters.AddWithValue(query.To);

        if (query.Job is { } job)
        {
            AppendJob(command, sql, job);
        }
        else
        {
            if (query.Environment is { } environment)
            {
                // A job whose sender names no environment belongs to every one, as on the resource lists.
                sql.Append(Condition(command, "(environment = {0} OR environment IS NULL)", environment));
            }

            if (query.Source is { } source)
            {
                sql.Append(Condition(command, "source = {0}", JobRuns.NameOf(source)));
            }

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                // The text is a parameter, and its LIKE wildcards are escaped, so it only ever matches itself.
                sql.Append(Condition(
                    command,
                    "(job_name ILIKE '%' || {0} || '%' OR block_name ILIKE '%' || {0} || '%')",
                    EscapeLike(query.Search.Trim())));
            }
        }

        sql.Append(" GROUP BY source, environment, block_kind, block_name, job_name");
        command.CommandText = sql.ToString();

        return command;
    }

    /// <summary>
    /// Builds the statement that lists one job's runs of a window, newest first, with whether each run's trace is
    /// still stored. The plan tests explain it.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="from">The start of the window, inclusive.</param>
    /// <param name="to">The end of the window, exclusive.</param>
    /// <param name="failedOnly">Whether only failed runs are listed.</param>
    /// <param name="limit">The most runs.</param>
    /// <returns>The command, without a connection, which the caller disposes.</returns>
    public static NpgsqlCommand CreateRunsCommand(
        JobKey job,
        DateTimeOffset from,
        DateTimeOffset to,
        bool failedOnly,
        int limit)
    {
        // The root span starts when the run does, so the trace is looked up by ID in the day of its start only.
        return CreateJobCommand(
            _runColumns
            + ", trace_id IS NOT NULL AND EXISTS (SELECT 1 FROM storage.span WHERE span.trace_id = job_run.trace_id "
            + "AND span.parent_span_id IS NULL AND span.start_time = job_run.started_at)",
            job,
            from,
            to,
            failedOnly,
            limit);
    }

    private static NpgsqlCommand CreateJobCommand(
        string columns,
        JobKey job,
        DateTimeOffset from,
        DateTimeOffset to,
        bool failedOnly,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(job);

        var command = new NpgsqlCommand();
        var sql = new StringBuilder(
            $"SELECT {columns} FROM storage.job_run WHERE started_at >= $1 AND started_at < $2");

        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        AppendJob(command, sql, job);

        if (failedOnly)
        {
            sql.Append(" AND status = 1");
        }

        command.Parameters.AddWithValue(limit);
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY started_at DESC, run_id DESC LIMIT ${command.Parameters.Count}");
        command.CommandText = sql.ToString();

        return command;
    }

    // The job's key, column by column, so the key index finds it; a job without an environment is matched by IS NULL,
    // which the index can read where IS NOT DISTINCT FROM could not.
    private static void AppendJob(NpgsqlCommand command, StringBuilder sql, JobKey job)
    {
        sql.Append(Condition(command, "source = {0}", JobRuns.NameOf(job.Source)));
        sql.Append(job.Environment is { } environment
            ? Condition(command, "environment = {0}", environment)
            : " AND environment IS NULL");
        sql.Append(Condition(command, "block_name = {0}", job.Block.Name));
        sql.Append(Condition(command, "job_name = {0}", job.Name));
    }

    private static string Condition(NpgsqlCommand command, string condition, object value)
    {
        command.Parameters.AddWithValue(value);

        return " AND " + string.Format(CultureInfo.InvariantCulture, condition, "$" + command.Parameters.Count);
    }

    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static double? Double(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
