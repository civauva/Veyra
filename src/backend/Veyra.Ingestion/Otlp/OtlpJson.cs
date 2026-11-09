using System.Text.Json.Nodes;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;

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
        JsonNode? root;

        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new OtlpFormatException("The body is not valid JSON: " + exception.Message);
        }

        foreach (var record in LogRecords(root))
        {
            HexToBase64(record, "traceId", "trace_id", 16);
            HexToBase64(record, "spanId", "span_id", 8);
        }

        try
        {
            return _parser.Parse<ExportLogsServiceRequest>(root?.ToJsonString() ?? "{}");
        }
        catch (InvalidJsonException exception)
        {
            throw new OtlpFormatException("The body is not an OTLP logs request: " + exception.Message);
        }
    }

    /// <summary>
    /// Writes a response as OTLP/JSON.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <returns>The JSON text.</returns>
    public static string Format(ExportLogsServiceResponse response) => JsonFormatter.Default.Format(response);

    private static IEnumerable<JsonObject> LogRecords(JsonNode? root) =>
        Items(root, "resourceLogs", "resource_logs")
            .SelectMany(resource => Items(resource, "scopeLogs", "scope_logs"))
            .SelectMany(scope => Items(scope, "logRecords", "log_records"))
            .OfType<JsonObject>();

    private static IEnumerable<JsonNode> Items(JsonNode? node, string camelName, string snakeName) =>
        ((node as JsonObject)?[camelName] as JsonArray ?? (node as JsonObject)?[snakeName] as JsonArray ?? [])
            .OfType<JsonNode>();

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
