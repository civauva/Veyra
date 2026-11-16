namespace Veyra.Core.Traces;

/// <summary>How many spans of a trace one service sent, for the list's ribbon of services.</summary>
/// <param name="Service">The service.</param>
/// <param name="Spans">Its spans in the trace.</param>
public sealed record TraceServiceShare(string Service, int Spans);
