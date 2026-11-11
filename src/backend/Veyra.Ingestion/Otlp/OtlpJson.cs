using System.Text.Json.Nodes;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Reads and writes OTLP/JSON, which differs from standard protobuf JSON in one way: trace and span IDs are hex
/// strings, not base64 (OTLP specification, "JSON Protobuf Encoding").
/// </summary>
internal static class OtlpJson
{
    private static readonly JsonParser _parser = new(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    /// <summary>
    /// Parses an OTLP/JSON logs request.
    /// </summary>
    /// <param name="json">The request body.</param>
    /// <returns>The request.</returns>
    /// <exception cref="OtlpFormatException">The JSON is not a valid OTLP logs request.</exception>
    public static ExportLogsServiceRequest ParseLogsRequest(string json)
    {
        var root = ParseJson(json);

        foreach (var record in LogRecords(root))
        {
            FixIds(record);
        }

        return Parse<ExportLogsServiceRequest>(root, "logs");
    }

    /// <summary>
    /// Parses an OTLP/JSON metrics request. Only exemplars carry trace and span IDs.
    /// </summary>
    /// <param name="json">The request body.</param>
    /// <returns>The request.</returns>
    /// <exception cref="OtlpFormatException">The JSON is not a valid OTLP metrics request.</exception>
    public static ExportMetricsServiceRequest ParseMetricsRequest(string json)
    {
        var root = ParseJson(json);

        foreach (var exemplar in Exemplars(root))
        {
            FixIds(exemplar);
        }

        return Parse<ExportMetricsServiceRequest>(root, "metrics");
    }

    /// <summary>
    /// Writes a response as OTLP/JSON.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON text.</returns>
    public static string Format(IMessage response) => JsonFormatter.Default.Format(response);

    private static JsonNode? ParseJson(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new OtlpFormatException("The body is not valid JSON: " + exception.Message);
        }
    }

    private static T Parse<T>(JsonNode? root, string signal)
        where T : IMessage, new()
    {
        try
        {
            return _parser.Parse<T>(root?.ToJsonString() ?? "{}");
        }
        catch (InvalidJsonException exception)
        {
            throw new OtlpFormatException($"The body is not an OTLP {signal} request: " + exception.Message);
        }
    }

    private static IEnumerable<JsonObject> LogRecords(JsonNode? root) =>
        Items(root, "resourceLogs", "resource_logs")
            .SelectMany(resource => Items(resource, "scopeLogs", "scope_logs"))
            .SelectMany(scope => Items(scope, "logRecords", "log_records"))
            .OfType<JsonObject>();

    private static IEnumerable<JsonObject> Exemplars(JsonNode? root) =>
        Items(root, "resourceMetrics", "resource_metrics")
            .SelectMany(resource => Items(resource, "scopeMetrics", "scope_metrics"))
            .SelectMany(scope => Items(scope, "metrics", "metrics"))
            .OfType<JsonObject>()
            .SelectMany(metric => metric
                .Where(property => property.Value is JsonObject)
                .Select(property => property.Value))
            .SelectMany(data => Items(data, "dataPoints", "data_points"))
            .SelectMany(point => Items(point, "exemplars", "exemplars"))
            .OfType<JsonObject>();

    private static IEnumerable<JsonNode> Items(JsonNode? node, string camelName, string snakeName) =>
        ((node as JsonObject)?[camelName] as JsonArray ?? (node as JsonObject)?[snakeName] as JsonArray ?? [])
            .OfType<JsonNode>();

    private static void FixIds(JsonObject item)
    {
        HexToBase64(item, "traceId", "trace_id", 16);
        HexToBase64(item, "spanId", "span_id", 8);
    }

    private static void HexToBase64(JsonObject record, string camelName, string snakeName, int byteLength)
    {
        foreach (var name in new[] { camelName, snakeName })
        {
            if (record[name] is JsonValue value
                && value.TryGetValue<string>(out var hex)
                && hex.Length == byteLength * 2)
            {
                try
                {
                    record[name] = Convert.ToBase64String(Convert.FromHexString(hex));
                }
                catch (FormatException)
                {
                    throw new OtlpFormatException($"{name} must be {byteLength * 2} hex characters.");
                }
            }
        }
    }
}
