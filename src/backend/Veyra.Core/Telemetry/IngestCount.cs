namespace Veyra.Core.Telemetry;

/// <summary>How much of one signal arrived in one step of a window (ADR 0023).</summary>
/// <param name="Start">The start of the step.</param>
/// <param name="Signal">The signal.</param>
/// <param name="Count">How many records, spans, or points the writers stored in the step.</param>
public sealed record IngestCount(DateTimeOffset Start, IngestSignal Signal, long Count);
