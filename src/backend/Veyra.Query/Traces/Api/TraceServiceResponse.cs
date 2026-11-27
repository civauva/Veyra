namespace Veyra.Query.Traces.Api;

/// <summary>A service that received requests in the window, with figures of its traces (ADR 0029).</summary>
/// <param name="Service">The service.</param>
/// <param name="Traces">How many of the traces read it received.</param>
/// <param name="P95Ms">The 95th percentile of their durations, in milliseconds.</param>
/// <param name="Failed">How many of them failed.</param>
public sealed record TraceServiceResponse(string Service, int Traces, double P95Ms, int Failed);
