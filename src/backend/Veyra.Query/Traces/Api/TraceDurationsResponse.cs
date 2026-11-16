namespace Veyra.Query.Traces.Api;

/// <summary>The durations of the newest traces of a window, for the list's chart.</summary>
/// <param name="Points">Every failed and slow trace read, and an even sample of the rest, oldest first.</param>
/// <param name="Read">How many traces were read.</param>
/// <param name="MaxRead">
/// The most a request reads; when <paramref name="Read"/> reaches it, older traces are left out.
/// </param>
/// <param name="P95Ms">The 95th percentile of the durations read, in milliseconds, or <see langword="null"/>.</param>
public sealed record TraceDurationsResponse(
    IReadOnlyList<TraceDurationPointResponse> Points,
    int Read,
    int MaxRead,
    double? P95Ms);
