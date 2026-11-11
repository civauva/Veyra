using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Turns an OTLP logs export request into Veyra's flat <see cref="LogRecord"/>s.
/// </summary>
/// <remarks>
/// Follows the OpenTelemetry logs data model: the record's time falls back to its observed time, then to the time
/// Veyra received it; a resource without <c>service.name</c> is <c>unknown_service</c>; IDs that are all zero mean
/// "none". Values that are not strings are kept as text, with arrays and maps as JSON.
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
            var service = resource.GetValueOrDefault("service.name") is { Length: > 0 } name
                ? name
                : OtlpValues.UnknownService;
            var environment = OtlpValues.Environment(resource);

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeName = scopeLogs.Scope?.Name;

                foreach (var log in scopeLogs.LogRecords)
                {
                    var observed = OtlpValues.FromUnixNanos(log.ObservedTimeUnixNano) ?? receivedAt;
                    var attributes = OtlpValues.ToDictionary(log.Attributes);

                    if (!string.IsNullOrEmpty(scopeName))
                    {
                        attributes.TryAdd("otel.scope.name", scopeName);
                    }

                    records.Add(new LogRecord(
                        Timestamp: OtlpValues.FromUnixNanos(log.TimeUnixNano) ?? observed,
                        ObservedTimestamp: observed,
                        Severity: LogSeverityExtensions.FromOtlpSeverityNumber((int)log.SeverityNumber),
                        SeverityText: string.IsNullOrEmpty(log.SeverityText) ? null : log.SeverityText,
                        Body: OtlpValues.ToText(log.Body),
                        ServiceName: service,
                        Environment: environment,
                        TraceId: ToHexId(log.TraceId),
                        SpanId: ToHexId(log.SpanId),
                        Attributes: attributes,
                        ResourceAttributes: resource));
                }
            }
        }

        return records;
    }

    private static string? ToHexId(ByteString id) =>
        id.IsEmpty || id.Span.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexStringLower(id.Span);
}
