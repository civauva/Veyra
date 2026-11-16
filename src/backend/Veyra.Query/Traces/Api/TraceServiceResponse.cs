namespace Veyra.Query.Traces.Api;

/// <summary>A service that received requests in the window.</summary>
/// <param name="Service">The service.</param>
/// <param name="Traces">How many of the traces read it received.</param>
public sealed record TraceServiceResponse(string Service, int Traces);
