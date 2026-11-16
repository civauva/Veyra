namespace Veyra.Core.Traces;

/// <summary>
/// Something that happened at a moment during a span. An exception is an event named <c>exception</c> with the
/// attributes <c>exception.type</c>, <c>exception.message</c>, and <c>exception.stacktrace</c>.
/// </summary>
/// <param name="Timestamp">When it happened.</param>
/// <param name="Name">What happened.</param>
/// <param name="Attributes">Its attributes. Non-string values are stored as JSON text, as on log records.</param>
public sealed record SpanEvent(DateTimeOffset Timestamp, string Name, IReadOnlyDictionary<string, string> Attributes);
