namespace Veyra.Core.Traces;

/// <summary>A service that received requests in a window, with figures of the traces read that it received.</summary>
/// <param name="Service">The service.</param>
/// <param name="Traces">Its traces among those read.</param>
/// <param name="Failed">How many of them failed (ADR 0029).</param>
/// <param name="P95">The 95th percentile of their durations, by nearest rank (ADR 0029).</param>
public sealed record TraceServiceCount(string Service, int Traces, int Failed, TimeSpan P95);
