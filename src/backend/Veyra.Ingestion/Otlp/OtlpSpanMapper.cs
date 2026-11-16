using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Veyra.Core.Traces;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;
using OtlpStatus = OpenTelemetry.Proto.Trace.V1.Status;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Turns OTLP trace requests into spans as Veyra stores them, within the limits of ADR 0014.
/// </summary>
/// <remarks>
/// A span keeps at most 128 attributes, 128 events, and 32 links, and attribute values up to 4 KB; exception stack
/// traces keep up to 32 KB. What is left out is counted on the span, as the OpenTelemetry limits do, so the trace view
/// can say so. A span without a trace ID, a span ID, or a start time cannot be placed in any trace and is left out.
/// </remarks>
internal static class OtlpSpanMapper
{
    /// <summary>The most attributes a span, an event, or a link keeps.</summary>
    public const int MaxAttributes = 128;

    /// <summary>The most events a span keeps.</summary>
    public const int MaxEvents = 128;

    /// <summary>The most links a span keeps.</summary>
    public const int MaxLinks = 32;

    /// <summary>The longest attribute value kept, in characters.</summary>
    public const int MaxValueLength = 4 * 1024;

    /// <summary>The longest exception stack trace kept, in characters.</summary>
    public const int MaxStackTraceLength = 32 * 1024;

    private const string _stackTrace = "exception.stacktrace";

    /// <summary>Maps every span of a request.</summary>
    /// <param name="request">The OTLP request.</param>
    /// <returns>The spans, and how many were left out because they could not be placed in a trace.</returns>
    public static MappedSpans Map(ExportTraceServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var spans = new List<Span>();
        var invalid = 0;

        foreach (var resourceSpans in request.ResourceSpans)
        {
            var resource = OtlpValues.ToDictionary(resourceSpans.Resource?.Attributes);
            var service = resource.GetValueOrDefault("service.name") is { Length: > 0 } name
                ? name
                : OtlpValues.UnknownService;
            var environment = OtlpValues.Environment(resource);
            var host = resource.GetValueOrDefault("host.name") is { Length: > 0 } hostName ? hostName : null;

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                var scopeName = scopeSpans.Scope?.Name;

                foreach (var span in scopeSpans.Spans)
                {
                    var traceId = HexId(span.TraceId, 16);
                    var spanId = HexId(span.SpanId, 8);
                    var start = OtlpValues.FromUnixNanos(span.StartTimeUnixNano);

                    if (traceId is null || spanId is null || start is null)
                    {
                        invalid++;
                        continue;
                    }

                    spans.Add(Map(span, traceId, spanId, start.Value, scopeName, service, environment, host, resource));
                }
            }
        }

        return new MappedSpans(spans, invalid);
    }

    private static Span Map(
        OtlpSpan span,
        string traceId,
        string spanId,
        DateTimeOffset start,
        string? scopeName,
        string service,
        string? environment,
        string? host,
        Dictionary<string, string> resource)
    {
        var attributes = Limited(span.Attributes, out var droppedAttributes);

        if (!string.IsNullOrEmpty(scopeName) && !attributes.ContainsKey("otel.scope.name"))
        {
            if (attributes.Count < MaxAttributes)
            {
                attributes["otel.scope.name"] = scopeName;
            }
            else
            {
                droppedAttributes++;
            }
        }

        var events = span.Events
            .Take(MaxEvents)
            .Select(@event => new SpanEvent(
                OtlpValues.FromUnixNanos(@event.TimeUnixNano) ?? start,
                @event.Name,
                Limited(@event.Attributes, out _)))
            .ToList();
        var links = span.Links
            .Take(MaxLinks)
            .Select(link => (TraceId: HexId(link.TraceId, 16), SpanId: HexId(link.SpanId, 8), link.Attributes))
            .Where(link => link.TraceId is not null && link.SpanId is not null)
            .Select(link => new SpanLink(link.TraceId!, link.SpanId!, Limited(link.Attributes, out _)))
            .ToList();

        // Nanoseconds to microseconds, the precision stored; a span that ends before it starts took no time.
        var end = span.EndTimeUnixNano > span.StartTimeUnixNano ? span.EndTimeUnixNano : span.StartTimeUnixNano;
        var duration = TimeSpan.FromMicroseconds((long)((end - span.StartTimeUnixNano) / 1_000));

        return new Span(
            traceId,
            spanId,
            HexId(span.ParentSpanId, 8),
            span.Name,
            Enum.IsDefined((SpanKind)span.Kind) ? (SpanKind)span.Kind : SpanKind.Unspecified,
            start,
            duration,
            span.Status?.Code switch
            {
                OtlpStatus.Types.StatusCode.Error => SpanStatus.Error,
                OtlpStatus.Types.StatusCode.Ok => SpanStatus.Ok,
                _ => SpanStatus.Unset,
            },
            string.IsNullOrEmpty(span.Status?.Message) ? null : span.Status.Message,
            service,
            environment,
            host,
            attributes,
            resource,
            events,
            links,
            new SpanDropped(
                droppedAttributes + (int)span.DroppedAttributesCount,
                Math.Max(0, span.Events.Count - MaxEvents) + (int)span.DroppedEventsCount,
                Math.Max(0, span.Links.Count - MaxLinks) + (int)span.DroppedLinksCount));
    }

    /// <summary>The first attributes up to the limit, each value cut to its length limit.</summary>
    private static Dictionary<string, string> Limited(
        IEnumerable<OpenTelemetry.Proto.Common.V1.KeyValue> attributes,
        out int dropped)
    {
        var all = OtlpValues.ToDictionary(attributes);
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in all)
        {
            if (kept.Count == MaxAttributes)
            {
                break;
            }

            var limit = key == _stackTrace ? MaxStackTraceLength : MaxValueLength;
            kept[key] = value.Length > limit ? value[..limit] : value;
        }

        dropped = all.Count - kept.Count;

        return kept;
    }

    /// <summary>
    /// An ID as lower-case hex, or <see langword="null"/> when it is missing, all zero, or the wrong length.
    /// </summary>
    private static string? HexId(ByteString id, int length) =>
        id.Length != length || id.Span.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexStringLower(id.Span);
}
