namespace Veyra.Core.Traces;

/// <summary>A page of the trace list, in the order the query asked for.</summary>
/// <param name="Items">The traces.</param>
/// <param name="Next">
/// Where the next page of the newest-first list starts, or <see langword="null"/> when this is the last or the list is
/// worst first.
/// </param>
/// <param name="NextWorst">
/// Where the next page of the worst-first list starts (ADR 0029), or <see langword="null"/> when this is the last or
/// the list is newest first.
/// </param>
public sealed record TracePage(
    IReadOnlyList<TraceSummary> Items,
    TraceCursor? Next,
    TraceWorstCursor? NextWorst = null);
