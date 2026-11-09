namespace Veyra.Core.Telemetry;

/// <summary>
/// A log record together with the ID Veyra gave it when storing it.
/// </summary>
/// <param name="Id">
/// The storage ID. It only orders records with equal timestamps; it is not meaningful otherwise.
/// </param>
/// <param name="Record">The record.</param>
public sealed record StoredLogRecord(long Id, LogRecord Record);
