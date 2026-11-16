namespace Veyra.Core.Traces;

/// <summary>
/// One trace of the list: its root span, and what its other spans add, counted when the page is read.
/// </summary>
/// <param name="TraceId">The trace ID, in lower-case hex.</param>
/// <param name="RootSpanId">The root span's ID, in lower-case hex.</param>
/// <param name="Name">The root span's name, which names the request or job.</param>
/// <param name="ServiceName">The service that received the request.</param>
/// <param name="Environment">Its environment, if any.</param>
/// <param name="Start">When the request started.</param>
/// <param name="Duration">How long the request took.</param>
/// <param name="Failed">Whether the request failed: its root span has error status.</param>
/// <param name="Spans">How many spans of the trace have arrived.</param>
/// <param name="FailedSpans">How many of them failed.</param>
/// <param name="Cause">Why it failed: the deepest failed span's service and message, if any span failed.</param>
/// <param name="Services">The services the trace went through, with their span counts, most spans first.</param>
public sealed record TraceSummary(
    string TraceId,
    string RootSpanId,
    string Name,
    string ServiceName,
    string? Environment,
    DateTimeOffset Start,
    TimeSpan Duration,
    bool Failed,
    int Spans,
    int FailedSpans,
    TraceFailure? Cause,
    IReadOnlyList<TraceServiceShare> Services);
