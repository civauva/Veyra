using Veyra.Core.Traces;

namespace Veyra.Query.Traces.Api;

/// <summary>One span of a trace.</summary>
/// <param name="SpanId">The span ID, 16 lower-case hex characters.</param>
/// <param name="ParentSpanId">The parent's span ID, or <see langword="null"/> for the root.</param>
/// <param name="Name">What the operation is.</param>
/// <param name="Kind">What the span does in its trace.</param>
/// <param name="Start">When it started.</param>
/// <param name="DurationMs">How long it took, in milliseconds to the microsecond.</param>
/// <param name="Status">How it ended.</param>
/// <param name="StatusMessage">Why it failed, when the sender said.</param>
/// <param name="Service">The service that sent it.</param>
/// <param name="Environment">Its environment, if any.</param>
/// <param name="Host">The host it ran on, if the sender said.</param>
/// <param name="Attributes">Its attributes.</param>
/// <param name="ResourceAttributes">The attributes of the resource that sent it.</param>
/// <param name="Events">Moments during the span, oldest first.</param>
/// <param name="Links">Pointers to other spans.</param>
/// <param name="Dropped">How much the sender or Veyra's limits left out.</param>
public sealed record SpanResponse(
    string SpanId,
    string? ParentSpanId,
    string Name,
    SpanKind Kind,
    DateTimeOffset Start,
    double DurationMs,
    SpanStatus Status,
    string? StatusMessage,
    string Service,
    string? Environment,
    string? Host,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyList<SpanEvent> Events,
    IReadOnlyList<SpanLink> Links,
    SpanDropped Dropped);
