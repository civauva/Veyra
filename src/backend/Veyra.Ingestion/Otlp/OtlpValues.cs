using System.Globalization;
using System.Text.Json;
using OpenTelemetry.Proto.Common.V1;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Conversions every OTLP signal shares: attributes to text, and OTLP's nanosecond timestamps.
/// </summary>
internal static class OtlpValues
{
    /// <summary>The name the OpenTelemetry specification gives a service that set no <c>service.name</c>.</summary>
    public const string UnknownService = "unknown_service";

    /// <summary>Turns attributes into text values. Duplicate keys are invalid OTLP; the last one wins.</summary>
    /// <param name="attributes">The attributes, or null for none.</param>
    /// <returns>The attributes by key.</returns>
    public static Dictionary<string, string> ToDictionary(IEnumerable<KeyValue>? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attribute in attributes ?? [])
        {
            result[attribute.Key] = ToText(attribute.Value);
        }

        return result;
    }

    /// <summary>
    /// The environment from resource attributes. <c>deployment.environment</c> was renamed to
    /// <c>deployment.environment.name</c>; older SDKs still send the old one.
    /// </summary>
    /// <param name="resource">The resource attributes.</param>
    /// <returns>The environment, or null when none was set.</returns>
    public static string? Environment(IReadOnlyDictionary<string, string> resource) =>
        resource.GetValueOrDefault("deployment.environment.name")
        ?? resource.GetValueOrDefault("deployment.environment");

    /// <summary>A value as text: strings as they are, numbers in invariant form, arrays and maps as JSON.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text; empty when there is no value.</returns>
    public static string ToText(AnyValue? value) => value?.ValueCase switch
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

    /// <summary>A time in nanoseconds since the Unix epoch.</summary>
    /// <param name="nanos">The nanoseconds; 0 means not set.</param>
    /// <returns>The time, or null when it was not set.</returns>
    public static DateTimeOffset? FromUnixNanos(ulong nanos) =>
        nanos == 0 ? null : DateTimeOffset.UnixEpoch.AddTicks((long)(nanos / 100));

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
}
