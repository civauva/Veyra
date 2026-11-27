namespace Veyra.Core.Traces;

/// <summary>The figures of the traces of a window under a query's filters (ADR 0029).</summary>
/// <remarks>
/// The counts are of every root span in the window. The percentiles are of the newest traces the duration chart reads,
/// so <see cref="P95"/> is the one the list's slow filter uses, and <see cref="Slow"/> counts the traces that filter
/// lists.
/// </remarks>
/// <param name="Traces">How many traces the window holds.</param>
/// <param name="Failed">How many of them failed: their root span has error status.</param>
/// <param name="Slow">How many took at least <see cref="P95"/>.</param>
/// <param name="P50">The median duration, or <see langword="null"/> when there are no traces.</param>
/// <param name="P95">The 95th percentile of the durations, or <see langword="null"/> when there are no traces.</param>
/// <param name="Services">How many services received the traces.</param>
/// <param name="PreviousTraces">
/// How many traces the equally long period just before the window holds, for the change; <see langword="null"/> when
/// that period would start before the earliest time there is.
/// </param>
public sealed record TraceFigures(
    int Traces,
    int Failed,
    int Slow,
    TimeSpan? P50,
    TimeSpan? P95,
    int Services,
    int? PreviousTraces);
