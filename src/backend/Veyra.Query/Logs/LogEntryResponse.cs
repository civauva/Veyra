using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>A stored log record, as the REST API returns it.</summary>
/// <param name="Id">The storage ID. Only meaningful for ordering records with equal timestamps.</param>
/// <param name="Timestamp">When the event happened.</param>
/// <param name="ObservedTimestamp">When the sender observed it.</param>
/// <param name="Severity">The coarse severity, for example <c>Error</c>.</param>
/// <param name="SeverityText">The severity as the sender wrote it.</param>
/// <param name="Body">The message.</param>
/// <param name="ServiceName">The service that wrote the record.</param>
/// <param name="Environment">The environment, for example <c>production</c>.</param>
/// <param name="TraceId">The trace the record belongs to, in lower-case hex.</param>
/// <param name="SpanId">The span the record belongs to, in lower-case hex.</param>
/// <param name="Attributes">The record's own attributes.</param>
/// <param name="ResourceAttributes">The attributes of the resource that wrote it.</param>
public sealed record LogEntryResponse(
    long Id,
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
