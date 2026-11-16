using Veyra.Core.Traces;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// The spans of an OTLP request, and how many were left out because they could not be placed in a trace.
/// </summary>
/// <param name="Spans">The spans to store.</param>
/// <param name="Invalid">Spans without a trace ID, a span ID, or a start time.</param>
internal sealed record MappedSpans(IReadOnlyList<Span> Spans, int Invalid);
