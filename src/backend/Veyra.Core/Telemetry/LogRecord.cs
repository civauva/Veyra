namespace Veyra.Core.Telemetry;

/// <summary>
/// A single normalized log record as Veyra stores and queries it.
/// Shaped after the OpenTelemetry log data model; attributes are kept flat and string-keyed.
/// </summary>
/// <param name="Timestamp">When the event happened, or when it was observed if the sender did not say.</param>
/// <param name="ObservedTimestamp">When the event was observed by the SDK or Collector that sent it.</param>
/// <param name="Severity">The coarse severity, derived from the OTLP severity number.</param>
/// <param name="SeverityText">The severity as the sender wrote it, for example <c>Warning</c> or <c>WARN</c>.</param>
/// <param name="Body">The message. Structured bodies are stored as JSON text.</param>
/// <param name="ServiceName">The <c>service.name</c> resource attribute, or <c>unknown_service</c>.</param>
/// <param name="Environment">
/// The <c>deployment.environment.name</c> resource attribute (or the older <c>deployment.environment</c>).
/// </param>
/// <param name="TraceId">The trace ID in lower-case hex, when the record belongs to a trace.</param>
/// <param name="SpanId">The span ID in lower-case hex, when the record belongs to a span.</param>
/// <param name="Attributes">The record's own attributes. Non-string values are stored as JSON text.</param>
/// <param name="ResourceAttributes">The attributes of the resource that produced the record.</param>
public sealed record LogRecord(
    DateTimeOffset Timestamp,
    DateTimeOffset ObservedTimestamp,
    LogSeverity Severity,
    string? SeverityText,
    string Body,
    string ServiceName,
    string? Environment,
    string? TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> ResourceAttributes);
