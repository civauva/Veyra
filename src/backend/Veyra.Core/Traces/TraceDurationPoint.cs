namespace Veyra.Core.Traces;

/// <summary>One trace on the list's duration chart.</summary>
/// <param name="TraceId">The trace ID, in lower-case hex.</param>
/// <param name="Start">When it started.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="Failed">Whether it failed.</param>
public sealed record TraceDurationPoint(string TraceId, DateTimeOffset Start, TimeSpan Duration, bool Failed);
