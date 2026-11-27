using Veyra.Core.Telemetry;

namespace Veyra.Query.Streams;

/// <summary>A log record just written, numbered in the order the hub received it.</summary>
/// <param name="Sequence">
/// Grows by one for every record written on this instance, so gaps show what a listener missed. It is not the storage
/// ID, which the writer does not learn.
/// </param>
/// <param name="Record">The record.</param>
internal sealed record LiveLogRecord(long Sequence, LogRecord Record);
