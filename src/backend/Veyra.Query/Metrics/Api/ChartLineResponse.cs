namespace Veyra.Query.Metrics.Api;

/// <summary>One line of a chart: one value per bucket of the window, oldest first.</summary>
/// <param name="Label">What the line shows, for example <c>user</c> or <c>p95</c>.</param>
/// <param name="Latest">The last value that is not null; null when there is none.</param>
/// <param name="Points">The points.</param>
public sealed record ChartLineResponse(string Label, double? Latest, IReadOnlyList<ChartPointResponse> Points)
{
    /// <summary>The average of the points that have a value, in their unit; null when none has one.</summary>
    /// <remarks>
    /// Worked out from the points the line carries, so it always agrees with what is drawn, and the browser never
    /// computes figures itself (ADR 0029).
    /// </remarks>
    public double? Average => Points.Average(point => point.Value);

    /// <summary>The highest point, in the points' unit; null when no point has a value.</summary>
    public double? Highest => Points.Max(point => point.Value);
}
