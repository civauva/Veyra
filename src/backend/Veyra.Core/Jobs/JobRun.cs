namespace Veyra.Core.Jobs;

/// <summary>One run of a job (ADR 0027), as the span writer or the activity writer keeps it.</summary>
/// <param name="Job">The job.</param>
/// <param name="RunId">
/// The run's ID: <c>cicd.pipeline.run.id</c> or the trace ID for a traced run, the job history's instance ID for SQL
/// Server Agent.
/// </param>
/// <param name="StartedAt">When it started.</param>
/// <param name="DurationSeconds">How long it took; <see langword="null"/> while it goes on.</param>
/// <param name="Status">How it stands.</param>
/// <param name="Steps">
/// A traced run's steps in start order; empty for SQL Server Agent, whose steps are read from its job history.
/// </param>
/// <param name="RecordsProcessed">How many records it processed, when the run says so.</param>
/// <param name="RecordsTotal">How many records it expected to process, when the run says so.</param>
/// <param name="Retries">How many times it was retried, when known.</param>
/// <param name="TraceId">
/// The trace of a traced run, as lower-case hex; <see langword="null"/> for SQL Server Agent.
/// </param>
/// <param name="Message">What it said about how it ended, such as an error, or <see langword="null"/>.</param>
public sealed record JobRun(
    JobKey Job,
    string RunId,
    DateTimeOffset StartedAt,
    double? DurationSeconds,
    JobRunStatus Status,
    IReadOnlyList<JobStep> Steps,
    long? RecordsProcessed,
    long? RecordsTotal,
    int? Retries,
    string? TraceId,
    string? Message)
{
    /// <summary>
    /// Whether the run's trace is still stored. Runs are kept longer than spans, so an older run's trace link says the
    /// trace has expired. Set when runs are read; always false for SQL Server Agent.
    /// </summary>
    public bool TraceKept { get; init; }
}
