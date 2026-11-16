namespace Veyra.Query.Traces.Api;

/// <summary>A whole trace: every span that has arrived, by start time.</summary>
/// <param name="TraceId">The trace ID.</param>
/// <param name="Spans">The spans.</param>
public sealed record TraceResponse(string TraceId, IReadOnlyList<SpanResponse> Spans);
