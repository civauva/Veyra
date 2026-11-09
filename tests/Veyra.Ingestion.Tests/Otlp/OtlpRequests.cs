using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Veyra.Ingestion.Tests.Otlp;

/// <summary>
/// Builds OTLP export requests the way the OpenTelemetry SDKs send them.
/// </summary>
internal static class OtlpRequests
{
    /// <summary>The time used by every test record: 2026-11-09 12:00:00 UTC.</summary>
    public static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A request with one resource, one scope, and the given records.</summary>
    public static ExportLogsServiceRequest Single(
        string service,
        string? environment,
        params OpenTelemetry.Proto.Logs.V1.LogRecord[] records)
    {
        var resource = new Resource();
        resource.Attributes.Add(Attribute("service.name", service));

        if (environment is not null)
        {
            resource.Attributes.Add(Attribute("deployment.environment.name", environment));
        }

        var scope = new ScopeLogs { Scope = new InstrumentationScope { Name = "Payments.Api" } };
        scope.LogRecords.AddRange(records);

        var resourceLogs = new ResourceLogs { Resource = resource };
        resourceLogs.ScopeLogs.Add(scope);

        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);

        return request;
    }

    /// <summary>A plain record with a string body at <see cref="Noon"/>.</summary>
    public static OpenTelemetry.Proto.Logs.V1.LogRecord Record(
        string body,
        SeverityNumber severity = SeverityNumber.Info) => new()
        {
            TimeUnixNano = UnixNanos(Noon),
            ObservedTimeUnixNano = UnixNanos(Noon.AddMilliseconds(5)),
            SeverityNumber = severity,
            SeverityText = severity.ToString(),
            Body = new AnyValue { StringValue = body },
        };

    /// <summary>A string attribute.</summary>
    public static KeyValue Attribute(string key, string value) =>
        new() { Key = key, Value = new AnyValue { StringValue = value } };

    /// <summary>Converts a time to OTLP's nanoseconds since the Unix epoch.</summary>
    public static ulong UnixNanos(DateTimeOffset time) => (ulong)(time - DateTimeOffset.UnixEpoch).Ticks * 100;

    /// <summary>Converts hex to the raw bytes OTLP uses for trace and span IDs.</summary>
    public static ByteString Bytes(string hex) => ByteString.CopyFrom(Convert.FromHexString(hex));
}
