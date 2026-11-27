namespace Veyra.Core.Telemetry;

/// <summary>
/// What to look for in the stored logs. Every filter except the time window is optional.
/// </summary>
/// <param name="From">The start of the time window, inclusive.</param>
/// <param name="To">The end of the time window, exclusive.</param>
/// <param name="MinimumSeverity">Only records at this severity or worse.</param>
/// <param name="Services">Only records from these services.</param>
/// <param name="Text">Words that must all appear in the message, ignoring case.</param>
/// <param name="TraceId">Only records of this trace, in lower-case hex.</param>
/// <param name="Limit">The most records to return.</param>
/// <param name="After">Where the previous page ended, or <see langword="null"/> for the first page.</param>
/// <param name="Environments">Only records from these environments.</param>
/// <param name="Attributes">Only records that have every one of these attributes with exactly this value.</param>
/// <param name="SpanId">Only records of this span, in lower-case hex, as a trace's chosen span asks (VEY-67).</param>
/// <param name="OldestFirst">
/// The oldest record first, as a trace lists its records (VEY-68); the newest first otherwise. Not with words.
/// </param>
/// <param name="Hosts">Only records from these hosts, by their <c>host.name</c> (ADR 0024).</param>
/// <param name="Levels">
/// Only records at exactly one of these severities (ADR 0024), beside <paramref name="MinimumSeverity"/>. An empty list
/// matches no record: it is how filters that exclude each other, such as two different traces, are told to storage.
/// </param>
/// <param name="Phrases">
/// Phrases the message must hold as written, ignoring case (ADR 0024). Their words are in <paramref name="Text"/> too,
/// so the word index finds the candidates and only those are checked for the phrase.
/// </param>
public sealed record LogQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    LogSeverity? MinimumSeverity,
    IReadOnlyList<string>? Services,
    string? Text,
    string? TraceId,
    int Limit,
    LogCursor? After,
    IReadOnlyList<string>? Environments = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? SpanId = null,
    bool OldestFirst = false,
    IReadOnlyList<string>? Hosts = null,
    IReadOnlyList<LogSeverity>? Levels = null,
    IReadOnlyList<string>? Phrases = null);
