namespace Veyra.Query.Jobs.Api;

/// <summary>One run on a job's screen.</summary>
/// <param name="RunId">
/// The run's ID: the pipeline run or the trace for a traced job, the job history's instance ID for SQL Server Agent.
/// </param>
/// <param name="StartedAt">When it started.</param>
/// <param name="DurationSeconds">How long it took; null while it goes on.</param>
/// <param name="Status"><c>succeeded</c>, <c>failed</c>, or <c>running</c>.</param>
/// <param name="Message">What it said about how it ended, such as an error; null for nothing.</param>
/// <param name="TraceId">The trace of a traced run, as lower-case hex; null for SQL Server Agent.</param>
/// <param name="TraceKept">Whether the trace is still stored; traces are kept a shorter time than runs.</param>
/// <param name="RecordsProcessed">How many records it processed, when the run says so.</param>
/// <param name="RecordsTotal">How many records it expected to process, when the run says so.</param>
/// <param name="Retries">How many times it was retried, when known.</param>
/// <param name="Steps">Its steps, in order.</param>
public sealed record JobRunDetailResponse(
    string RunId,
    DateTimeOffset StartedAt,
    double? DurationSeconds,
    string Status,
    string? Message,
    string? TraceId,
    bool TraceKept,
    long? RecordsProcessed,
    long? RecordsTotal,
    int? Retries,
    IReadOnlyList<JobRunStepResponse> Steps);
