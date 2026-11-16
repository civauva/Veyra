namespace Veyra.Core.Traces;

/// <summary>
/// One operation of a trace, as Veyra stores and queries it (ADR 0014). Shaped after the OpenTelemetry span; the
/// resource's service, environment, and host are lifted out as on log records.
/// </summary>
/// <param name="TraceId">The trace ID, 32 lower-case hex characters.</param>
/// <param name="SpanId">The span ID, 16 lower-case hex characters.</param>
/// <param name="ParentSpanId">The parent's span ID, or <see langword="null"/> for the trace's root span.</param>
/// <param name="Name">What the operation is, for example <c>POST /api/checkout</c>.</param>
/// <param name="Kind">What the span does in its trace.</param>
/// <param name="Start">When it started.</param>
/// <param name="Duration">How long it took, to the microsecond.</param>
/// <param name="Status">How it ended.</param>
/// <param name="StatusMessage">Why it failed, when the sender said.</param>
/// <param name="ServiceName">The <c>service.name</c> resource attribute, or <c>unknown_service</c>.</param>
/// <param name="Environment">The deployment environment of the resource, if any.</param>
/// <param name="HostName">The <c>host.name</c> resource attribute, if any.</param>
/// <param name="Attributes">The span's own attributes, at most 128 (ADR 0014).</param>
/// <param name="ResourceAttributes">The attributes of the resource that produced the span.</param>
/// <param name="Events">Moments during the span, at most 128, oldest first.</param>
/// <param name="Links">Pointers to other spans, at most 32.</param>
/// <param name="Dropped">How many attributes, events, and links the sender or Veyra left out.</param>
public sealed record Span(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    SpanKind Kind,
    DateTimeOffset Start,
    TimeSpan Duration,
    SpanStatus Status,
    string? StatusMessage,
    string ServiceName,
    string? Environment,
    string? HostName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyList<SpanEvent> Events,
    IReadOnlyList<SpanLink> Links,
    SpanDropped Dropped);
