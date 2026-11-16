namespace Veyra.Core.Traces;

/// <summary>A page of the trace list, newest first.</summary>
/// <param name="Items">The traces.</param>
/// <param name="Next">Where the next page starts, or <see langword="null"/> when this is the last.</param>
public sealed record TracePage(IReadOnlyList<TraceSummary> Items, TraceCursor? Next);
