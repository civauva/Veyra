namespace Veyra.Query.Traces.Api;

/// <summary>The figures of the traces in a window under the list's filters, counted on the server (ADR 0029).</summary>
/// <param name="Traces">How many traces the window holds.</param>
/// <param name="Failed">How many of them failed.</param>
/// <param name="Slow">
/// How many took at least <paramref name="P95Ms"/>: the traces the list shows with <c>slow=true</c>.
/// </param>
/// <param name="P50Ms">The median duration in milliseconds, or <see langword="null"/> when there are no traces.</param>
/// <param name="P95Ms">
/// The 95th percentile of the durations in milliseconds, as the duration chart has it, or <see langword="null"/>.
/// </param>
/// <param name="Services">How many services received the traces.</param>
/// <param name="PreviousTraces">
/// How many traces the equally long period just before the window holds, under the same filters, for the change;
/// <see langword="null"/> when that period would start before the earliest time there is.
/// </param>
public sealed record TraceFiguresResponse(
    int Traces,
    int Failed,
    int Slow,
    double? P50Ms,
    double? P95Ms,
    int Services,
    int? PreviousTraces);
