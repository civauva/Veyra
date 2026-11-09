using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
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
    private const string _unknownService = "unknown_service";

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
            var resource = ToDictionary(resourceLogs.Resource?.Attributes);
            var service = resource.GetValueOrDefault("service.name") is { Length: > 0 } name ? name : _unknownService;

            // deployment.environment was renamed to deployment.environment.name; older SDKs still send the old one.
            var environment = resource.GetValueOrDefault("deployment.environment.name")
                ?? resource.GetValueOrDefault("deployment.environment");

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeName = scopeLogs.Scope?.Name;

                foreach (var log in scopeLogs.LogRecords)
                {
                    var observed = FromUnixNanos(log.ObservedTimeUnixNano) ?? receivedAt;
                    var attributes = ToDictionary(log.Attributes);

                    if (!string.IsNullOrEmpty(scopeName))
                    {
                        attributes.TryAdd("otel.scope.name", scopeName);
                    }

                    records.Add(new LogRecord(
                        Timestamp: FromUnixNanos(log.TimeUnixNano) ?? observed,
                        ObservedTimestamp: observed,
                        Severity: LogSeverityExtensions.FromOtlpSeverityNumber((int)log.SeverityNumber),
                        SeverityText: string.IsNullOrEmpty(log.SeverityText) ? null : log.SeverityText,
                        Body: ToText(log.Body),
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

    private static Dictionary<string, string> ToDictionary(IEnumerable<KeyValue>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attribute in attributes ?? [])
        {
            // Duplicate keys are invalid OTLP; the last one wins, as in most SDKs.
            result[attribute.Key] = ToText(attribute.Value);
        }

        return result;
    }

    private static string ToText(AnyValue? value) => value?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString("R", CultureInfo.InvariantCulture),
        AnyValue.ValueOneofCase.BytesValue => value.BytesValue.ToBase64(),
        AnyValue.ValueOneofCase.ArrayValue or AnyValue.ValueOneofCase.KvlistValue =>
            JsonSerializer.Serialize(ToJson(value)),
        _ => string.Empty,
    };

    // Arrays and maps keep their structure as JSON, with numbers and booleans as JSON values, not strings.
    private static object? ToJson(AnyValue? value) => value?.ValueCase switch
    {
        AnyValue.ValueOneofCase.StringValue => value.StringValue,
        AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
        AnyValue.ValueOneofCase.IntValue => value.IntValue,
        AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
        AnyValue.ValueOneofCase.BytesValue => value.BytesValue.ToBase64(),
        AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values.Select(ToJson).ToList(),
        AnyValue.ValueOneofCase.KvlistValue =>
            value.KvlistValue.Values.ToDictionary(pair => pair.Key, pair => ToJson(pair.Value)),
        _ => null,
    };

    private static DateTimeOffset? FromUnixNanos(ulong nanos) =>
        nanos == 0 ? null : DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));

    private static string? ToHexId(ByteString id) =>
        id.IsEmpty || id.Span.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexStringLower(id.Span);
}
