namespace Veyra.Core.Traces;

/// <summary>
/// The durations of the newest traces of a window, for the list's chart and its "slow" filter. At most a fixed number
/// of the newest traces is read, so the chart costs the same however busy the window was.
/// </summary>
/// <param name="Points">The traces drawn: every failed and slow one, and an even sample of the rest.</param>
/// <param name="Read">How many traces were read; less than in the window when it was busier than the limit.</param>
/// <param name="P95">The 95th percentile of their durations, or <see langword="null"/> when there were none.</param>
/// <param name="Failed">How many traces read failed, though the points hold fewer (VEY-68).</param>
/// <param name="Slow">
/// How many traces read did not fail and took at least the 95th percentile, though the points hold fewer (VEY-68).
/// </param>
public sealed record TraceDurations(
    IReadOnlyList<TraceDurationPoint> Points,
    int Read,
    TimeSpan? P95,
    int Failed = 0,
    int Slow = 0);
