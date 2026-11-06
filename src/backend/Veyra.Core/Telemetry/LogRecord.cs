namespace Veyra.Core.Telemetry;

/// <summary>
/// A single normalized log record as Veyra stores and queries it.
/// Shaped after the OpenTelemetry log data model; attributes are kept flat and string-keyed.
/// </summary>
public sealed record LogRecord(
    DateTimeOffset Timestamp,
    DateTimeOffset ObservedTimestamp,
    LogSeverity Severity,
    string? SeverityText,
    string Body,
    string ServiceName,
    string? TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> ResourceAttributes);
