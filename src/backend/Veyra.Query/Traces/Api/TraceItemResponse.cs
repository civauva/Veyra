namespace Veyra.Query.Traces.Api;

/// <summary>One trace of the list: its root span, and what its other spans add.</summary>
/// <param name="TraceId">The trace ID, 32 lower-case hex characters.</param>
/// <param name="RootSpanId">The root span's ID.</param>
/// <param name="Name">The root span's name, which names the request or job.</param>
/// <param name="Service">The service that received the request.</param>
/// <param name="Environment">Its environment, if any.</param>
/// <param name="Start">When the request started.</param>
/// <param name="DurationMs">How long it took, in milliseconds to the microsecond.</param>
/// <param name="Failed">Whether the request failed: its root span has error status.</param>
/// <param name="Spans">How many spans of the trace have arrived.</param>
/// <param name="FailedSpans">How many of them failed.</param>
/// <param name="Cause">Why it failed, from its deepest failed span, if any span failed.</param>
/// <param name="Services">The services it went through, with their span counts, most spans first.</param>
public sealed record TraceItemResponse(
    string TraceId,
    string RootSpanId,
    string Name,
    string Service,
    string? Environment,
    DateTimeOffset Start,
    double DurationMs,
    bool Failed,
    int Spans,
    int FailedSpans,
    TraceCauseResponse? Cause,
    IReadOnlyList<TraceShareResponse> Services);
