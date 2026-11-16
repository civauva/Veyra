namespace Veyra.Query.Traces.Api;

/// <summary>One trace on the duration chart.</summary>
/// <param name="TraceId">The trace ID, so a click opens it.</param>
/// <param name="Start">When it started.</param>
/// <param name="DurationMs">How long it took, in milliseconds.</param>
/// <param name="Failed">Whether it failed.</param>
public sealed record TraceDurationPointResponse(string TraceId, DateTimeOffset Start, double DurationMs, bool Failed);
