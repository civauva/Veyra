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
    IReadOnlyDictionary<string, string>? Attributes = null);
