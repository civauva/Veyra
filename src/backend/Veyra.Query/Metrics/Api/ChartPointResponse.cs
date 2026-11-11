namespace Veyra.Query.Metrics.Api;

/// <summary>One point of a chart line.</summary>
/// <param name="At">The start of the bucket.</param>
/// <param name="Value">The value; null when there was nothing to compute it from.</param>
public sealed record ChartPointResponse(DateTimeOffset At, double? Value);
