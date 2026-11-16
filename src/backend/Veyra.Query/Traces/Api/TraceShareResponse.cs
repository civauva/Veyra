namespace Veyra.Query.Traces.Api;

/// <summary>How many spans of a trace one service sent.</summary>
/// <param name="Service">The service.</param>
/// <param name="Spans">Its spans in the trace.</param>
public sealed record TraceShareResponse(string Service, int Spans);
