namespace Veyra.Query.Incidents.Api;

/// <summary>
/// The path that failed (ADR 0026): a failing trace's critical path, or a failed SQL Agent job run's steps.
/// </summary>
/// <param name="Source"><c>trace</c> or <c>job</c>.</param>
/// <param name="TraceId">The trace, for a trace's path.</param>
/// <param name="JobName">The job, for a job run's steps.</param>
/// <param name="Steps">The steps, in the order they ran.</param>
public sealed record ExecutionPathResponse(
    string Source,
    string? TraceId,
    string? JobName,
    IReadOnlyList<ExecutionStepResponse> Steps);
