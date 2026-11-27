using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>
/// The filters every logs endpoint takes, as the query string gave them: the search, its counts, its export, and the
/// live stream. One record, so a new filter reaches all of them at once.
/// </summary>
/// <param name="Severity">The lowest severity, from <c>severity</c>.</param>
/// <param name="Service">The services, from repeated <c>service</c>.</param>
/// <param name="Text">The words of <c>text</c>.</param>
/// <param name="TraceId">The trace, from <c>traceId</c>.</param>
/// <param name="SpanId">The span, from <c>spanId</c>.</param>
/// <param name="Environment">The environments, from repeated <c>environment</c>.</param>
/// <param name="Attribute">The attribute filters written <c>key:value</c>, from repeated <c>attribute</c>.</param>
/// <param name="Host">The hosts, from repeated <c>host</c> (ADR 0024).</param>
/// <param name="Level">The exact levels, from repeated <c>level</c> (ADR 0024).</param>
/// <param name="Q">The query of the log bar, from <c>q</c> (ADR 0024).</param>
internal sealed record LogFilterParameters(
    LogSeverity? Severity,
    string[]? Service,
    string? Text,
    string? TraceId,
    string? SpanId,
    string[]? Environment,
    string[]? Attribute,
    string[]? Host,
    LogSeverity[]? Level,
    string? Q);
