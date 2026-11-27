using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Query.Incidents.Api;
using Veyra.Query.Jobs;
using Veyra.Query.Traces;

namespace Veyra.Query.Incidents;

/// <summary>
/// Works out the path that failed and the exception behind it (ADR 0026), from a failing trace, from a failed SQL Agent
/// job run, or from the logs. Pure; the builder reads what it needs.
/// </summary>
internal static class IncidentPaths
{
    /// <summary>The kind of record a run or step of a SQL Agent job is.</summary>
    public const string JobKind = "sqlserver.job";

    /// <summary>The attribute OpenTelemetry puts an exception's type in.</summary>
    public const string ExceptionType = "exception.type";

    /// <summary>The attribute OpenTelemetry puts an exception's message in.</summary>
    public const string ExceptionMessage = "exception.message";

    /// <summary>The attribute OpenTelemetry puts an exception's stack trace in.</summary>
    public const string ExceptionStackTrace = "exception.stacktrace";

    /// <summary>
    /// The critical path of a trace, root first, as the trace screen marks it (<see cref="TraceView"/>): the spans
    /// that decided how long the request took, which is where a failure shows.
    /// </summary>
    /// <param name="traceId">The trace.</param>
    /// <param name="spans">Its spans, at least one.</param>
    /// <returns>The path.</returns>
    public static ExecutionPathResponse Trace(string traceId, IReadOnlyList<Span> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        var ordered = Ordered(spans);
        var byId = ordered.ToDictionary(span => span.SpanId, StringComparer.Ordinal);

        return new ExecutionPathResponse(
            "trace",
            traceId,
            null,
            [
                .. TraceView.CriticalPath(ordered)
                    .Select(id => byId[id])
                    .Select(span => new ExecutionStepResponse(
                        span.Name,
                        span.ServiceName,
                        span.Duration.TotalMilliseconds,
                        Status(span.Status),
                        span.StatusMessage)),
            ]);
    }

    /// <summary>
    /// The exception of a trace's cause: the failed span none of whose children failed, read from its
    /// <c>exception</c> event or, where a sender puts them there, its attributes.
    /// </summary>
    /// <param name="traceId">The trace.</param>
    /// <param name="spans">Its spans.</param>
    /// <returns>The exception, or <see langword="null"/> when no span failed or the cause says nothing.</returns>
    public static ObservedExceptionResponse? TraceException(string traceId, IReadOnlyList<Span> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0 || TraceView.Cause(Ordered(spans)) is not { } cause)
        {
            return null;
        }

        var attributes = cause.Events.LastOrDefault(e => e.Name == "exception")?.Attributes ?? cause.Attributes;
        var message = attributes.GetValueOrDefault(ExceptionMessage) ?? cause.StatusMessage;

        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        return new ObservedExceptionResponse(
            attributes.GetValueOrDefault(ExceptionType),
            message,
            attributes.GetValueOrDefault(ExceptionStackTrace),
            cause.ServiceName,
            traceId,
            "trace");
    }

    /// <summary>
    /// The exception behind the most frequent error message of the affected services, with the stack trace and type of
    /// the first record of that message that carries them.
    /// </summary>
    /// <param name="message">The most frequent error message.</param>
    /// <param name="records">Error records of the window to find its details in.</param>
    /// <returns>The exception, with only the message when no record of it says more.</returns>
    public static ObservedExceptionResponse LogException(string message, IReadOnlyList<LogRecord> records)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(records);

        var same = records.Where(record => string.Equals(record.Body, message, StringComparison.Ordinal)).ToList();
        var detailed = same.FirstOrDefault(record => record.Attributes.ContainsKey(ExceptionStackTrace));
        var any = detailed ?? same.FirstOrDefault();

        return new ObservedExceptionResponse(
            detailed?.Attributes.GetValueOrDefault(ExceptionType),
            message,
            detailed?.Attributes.GetValueOrDefault(ExceptionStackTrace),
            any?.ServiceName,
            any?.TraceId,
            "logs");
    }

    /// <summary>
    /// The steps of a job's latest failed run, from the agent's job history, as the server's Jobs tab reads them
    /// (ADR 0020): a run is the record of step 0, its steps the records of the same job that ran within its duration.
    /// </summary>
    /// <param name="job">The job's name.</param>
    /// <param name="records">The server's job records around the window, of every job.</param>
    /// <param name="before">Only runs up to this time, the end of the window.</param>
    /// <returns>The path, or <see langword="null"/> when the job has no failed run.</returns>
    public static ExecutionPathResponse? Job(string job, IReadOnlyList<ActivityRecord> records, DateTimeOffset before)
    {
        ArgumentNullException.ThrowIfNull(records);

        var own = records
            .Where(record => record.Attributes.GetValueOrDefault("job_name") == job && record.SampledAt <= before)
            .ToList();
        var run = own
            .Where(record => record.Attributes.GetValueOrDefault("step_id") == "0" && Failed(record))
            .MaxBy(record => record.SampledAt);

        if (run is null)
        {
            return null;
        }

        var length = Number(run, "duration");
        var steps = own
            .Where(step => step.Attributes.GetValueOrDefault("step_id") != "0"
                && Math.Abs((step.SampledAt - run.SampledAt).TotalSeconds) <= length + 1)
            .OrderBy(step => Number(step, "step_id"))
            .Select(step => new ExecutionStepResponse(
                step.Attributes.GetValueOrDefault("step_name")
                    ?? $"Step {step.Attributes.GetValueOrDefault("step_id")}",
                null,
                Number(step, "duration") * 1000,
                Succeeded(step) ? "ok" : "error",
                string.IsNullOrEmpty(step.Body) ? null : step.Body))
            .ToList();

        // A run whose steps were not kept still says how it ended.
        if (steps.Count == 0)
        {
            steps.Add(new ExecutionStepResponse(job, null, length * 1000, "error", run.Body));
        }

        return new ExecutionPathResponse("job", null, job, steps);
    }

    /// <summary>
    /// The steps of a failed job run, from the job runs Veyra keeps (ADR 0027): a traced run's own steps, or a SQL
    /// Agent run's steps from its job history, matched by time as the server's Jobs tab matches them.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="records">For a SQL Agent run, its server's job records around the run; ignored otherwise.</param>
    /// <returns>The path; a run whose steps were not kept still says how it ended.</returns>
    public static ExecutionPathResponse JobRun(JobRun run, IReadOnlyList<ActivityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(records);

        var traced = run.Job.Source == JobSource.Trace;
        var steps = traced ? run.Steps : AgentSteps.For(run, records).Steps;
        var service = traced ? run.Job.Block.Name : null;
        var path = steps
            .Select(step => new ExecutionStepResponse(
                step.Name,
                service,
                step.DurationSeconds * 1000,
                step.Status switch
                {
                    JobRunStatus.Succeeded => "ok",
                    JobRunStatus.Failed => "error",
                    _ => "unset",
                },
                step.Message))
            .ToList();

        if (path.Count == 0)
        {
            path.Add(new ExecutionStepResponse(
                run.Job.Name,
                service,
                (run.DurationSeconds ?? 0) * 1000,
                "error",
                run.Message));
        }

        return new ExecutionPathResponse("job", run.TraceId, run.Job.Name, path);
    }

    private static List<Span> Ordered(IReadOnlyList<Span> spans) =>
        [.. spans.OrderBy(span => span.Start).ThenBy(span => span.SpanId, StringComparer.Ordinal)];

    private static string Status(SpanStatus status) => status switch
    {
        SpanStatus.Ok => "ok",
        SpanStatus.Error => "error",
        _ => "unset",
    };

    // SQL Server's run_status: 1 succeeded; 0 failed, 2 retried, 3 cancelled, 4 still running.
    private static bool Succeeded(ActivityRecord record) => record.Attributes.GetValueOrDefault("run_status") == "1";

    private static bool Failed(ActivityRecord record) => record.Attributes.GetValueOrDefault("run_status") == "0";

    private static double Number(ActivityRecord record, string key) =>
        double.TryParse(
            record.Attributes.GetValueOrDefault(key),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
