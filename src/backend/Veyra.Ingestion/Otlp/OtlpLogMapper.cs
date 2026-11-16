using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Turns an OTLP logs export request into Veyra's flat <see cref="LogRecord"/>s.
/// </summary>
/// <remarks>
/// Follows the OpenTelemetry logs data model: the record's time falls back to its observed time, then to the time
/// Veyra received it; a resource that names no block is <c>unknown_service</c>; IDs that are all zero mean
/// "none". Values that are not strings are kept as text, with arrays and maps as JSON. The record is filed under the
/// block its resource names (ADR 0018), and an event keeps its name as the <c>event.name</c> attribute, which tells a
/// database's query samples from its top queries.
/// </remarks>
internal static class OtlpLogMapper
{
    /// <summary>
    /// Maps every record in the request.
    /// </summary>
    /// <param name="request">The decoded OTLP request.</param>
    /// <param name="receivedAt">When Veyra received the request, used when the sender gave no time at all.</param>
    /// <returns>The records in the order they appear in the request.</returns>
    public static List<LogRecord> Map(ExportLogsServiceRequest request, DateTimeOffset receivedAt)
    {
        var records = new List<LogRecord>();

        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resource = OtlpValues.ToDictionary(resourceLogs.Resource?.Attributes);
            var block = TelemetryBlock.Of(resource);
            var host = resource.GetValueOrDefault("host.name") is { Length: > 0 } name ? name : null;
            var environment = OtlpValues.Environment(resource);

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeName = scopeLogs.Scope?.Name;

                // A Collector's own logs name the component that failed only here, which Veyra's agents rely on
                // (ADR 0019). The record's own attributes win over its scope's.
                var scopeAttributes = OtlpValues.ToDictionary(scopeLogs.Scope?.Attributes);

                foreach (var log in scopeLogs.LogRecords)
                {
                    var observed = OtlpValues.FromUnixNanos(log.ObservedTimeUnixNano) ?? receivedAt;
                    var attributes = OtlpValues.ToDictionary(log.Attributes);

                    if (!string.IsNullOrEmpty(scopeName))
                    {
                        attributes.TryAdd("otel.scope.name", scopeName);
                    }

                    foreach (var (key, value) in scopeAttributes)
                    {
                        attributes.TryAdd(key, value);
                    }

                    if (!string.IsNullOrEmpty(log.EventName))
                    {
                        attributes.TryAdd("event.name", log.EventName);
                    }

                    records.Add(new LogRecord(
                        Timestamp: OtlpValues.FromUnixNanos(log.TimeUnixNano) ?? observed,
                        ObservedTimestamp: observed,
                        Severity: LogSeverityExtensions.FromOtlpSeverityNumber((int)log.SeverityNumber),
                        SeverityText: string.IsNullOrEmpty(log.SeverityText) ? null : log.SeverityText,
                        Body: OtlpValues.ToText(log.Body),
                        ServiceName: block.Name,
                        Environment: environment,
                        TraceId: ToHexId(log.TraceId),
                        SpanId: ToHexId(log.SpanId),
                        Attributes: attributes,
                        ResourceAttributes: resource,
                        BlockKind: block.Kind,
                        HostName: host));
                }
            }
        }

        return records;
    }

    private static string? ToHexId(ByteString id) =>
        id.IsEmpty || id.Span.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexStringLower(id.Span);
}
