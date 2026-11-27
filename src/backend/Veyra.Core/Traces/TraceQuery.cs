namespace Veyra.Core.Traces;

/// <summary>Which traces to list: a time window and filters on each trace's root span.</summary>
/// <param name="From">The start of the window, inclusive, on the root span's start.</param>
/// <param name="To">The end of the window, exclusive.</param>
/// <param name="Services">Only traces whose root span came from one of these services.</param>
/// <param name="Environments">Only traces whose root span came from one of these environments.</param>
/// <param name="FailedOnly">Only traces whose request failed: the root span has error status.</param>
/// <param name="MinDuration">Only traces that took at least this long.</param>
/// <param name="MaxDuration">Only traces that took at most this long.</param>
/// <param name="Name">Only traces whose root span's name contains this, ignoring case.</param>
/// <param name="Attributes">Only traces whose root span has all these attributes.</param>
/// <param name="Limit">The most traces on a page.</param>
/// <param name="After">The page of the newest-first list to continue, or <see langword="null"/> for its first.</param>
/// <param name="Order">The order of the list; the chart and the services always read the newest traces.</param>
/// <param name="AfterWorst">
/// The page of the worst-first list to continue (ADR 0029), or <see langword="null"/> for its first.
/// </param>
public sealed record TraceQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<string>? Services = null,
    IReadOnlyList<string>? Environments = null,
    bool FailedOnly = false,
    TimeSpan? MinDuration = null,
    TimeSpan? MaxDuration = null,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    int Limit = 50,
    TraceCursor? After = null,
    TraceOrder Order = TraceOrder.Newest,
    TraceWorstCursor? AfterWorst = null);
