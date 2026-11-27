namespace Veyra.Query.Incidents.Api;

/// <summary>One step of the path that failed: a span of the trace's critical path, or a step of a job run.</summary>
/// <param name="Name">The span's or the job step's name.</param>
/// <param name="Service">The service that ran it; <see langword="null"/> for a job step.</param>
/// <param name="DurationMs">How long it took, in milliseconds.</param>
/// <param name="Status"><c>ok</c>, <c>error</c>, or <c>unset</c> when the sender did not say.</param>
/// <param name="Detail">The span's status message, or the job step's message.</param>
public sealed record ExecutionStepResponse(
    string Name,
    string? Service,
    double? DurationMs,
    string Status,
    string? Detail);
