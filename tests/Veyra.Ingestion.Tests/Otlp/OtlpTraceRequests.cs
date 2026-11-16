using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Otlp;

/// <summary>Builds OTLP trace requests for the tests.</summary>
internal static class OtlpTraceRequests
{
    public const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    public static ExportTraceServiceRequest Spans(string service, string? environment, params Span[] spans)
    {
        var resource = new Resource();
        resource.Attributes.Add(Attribute("service.name", service));
        resource.Attributes.Add(Attribute("host.name", "web-02"));

        if (environment is not null)
        {
            resource.Attributes.Add(Attribute("deployment.environment.name", environment));
        }

        var scope = new ScopeSpans { Scope = new InstrumentationScope { Name = "Payments.Api" } };
        scope.Spans.AddRange(spans);

        var resourceSpans = new ResourceSpans { Resource = resource };
        resourceSpans.ScopeSpans.Add(scope);

        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpans);

        return request;
    }

    public static Span OneSpan(string spanId, string? parentId = null, string name = "POST /payments") => new()
    {
        TraceId = Bytes(TraceId),
        SpanId = Bytes(spanId),
        ParentSpanId = parentId is null ? Google.Protobuf.ByteString.Empty : Bytes(parentId),
        Name = name,
        Kind = Span.Types.SpanKind.Server,
        StartTimeUnixNano = UnixNanos(Noon),
        EndTimeUnixNano = UnixNanos(Noon) + 1_234_567_000,
    };
}
