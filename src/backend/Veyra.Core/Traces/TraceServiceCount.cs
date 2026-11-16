namespace Veyra.Core.Traces;

/// <summary>A service that received requests in a window, with how many of the traces read it received.</summary>
/// <param name="Service">The service.</param>
/// <param name="Traces">Its traces among those read.</param>
public sealed record TraceServiceCount(string Service, int Traces);
