namespace Veyra.Query.Traces.Api;

/// <summary>A whole trace: the spans that have arrived, by start time, and the figures of all of them.</summary>
/// <param name="TraceId">The trace ID.</param>
/// <param name="Spans">The spans; with <c>errorsOnly</c>, only the failed ones and those above them (VEY-67).</param>
/// <param name="Summary">The figures of every span, whatever the view.</param>
public sealed record TraceResponse(string TraceId, IReadOnlyList<SpanResponse> Spans, TraceSummaryResponse Summary);
