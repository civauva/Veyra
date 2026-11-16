namespace Veyra.Core.Traces;

/// <summary>
/// A pointer from a span to another span, in the same trace or another, for example to the messages of a batch.
/// </summary>
/// <param name="TraceId">The other span's trace ID, in lower-case hex.</param>
/// <param name="SpanId">The other span's ID, in lower-case hex.</param>
/// <param name="Attributes">The link's attributes.</param>
public sealed record SpanLink(string TraceId, string SpanId, IReadOnlyDictionary<string, string> Attributes);
