namespace Veyra.Query.Metrics.Api;

/// <summary>One line of a chart: one value per bucket of the window, oldest first.</summary>
/// <param name="Label">What the line shows, for example <c>user</c> or <c>p95</c>.</param>
/// <param name="Latest">The last value that is not null; null when there is none.</param>
/// <param name="Points">The points.</param>
public sealed record ChartLineResponse(string Label, double? Latest, IReadOnlyList<ChartPointResponse> Points);
