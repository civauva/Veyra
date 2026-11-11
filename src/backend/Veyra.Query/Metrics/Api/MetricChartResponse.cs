using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics.Api;

/// <summary>One metric charted for the metric browser.</summary>
/// <param name="Name">The metric name.</param>
/// <param name="Kind">Gauge, sum, or histogram.</param>
/// <param name="Unit">The unit as sent; a rate adds <c>/s</c>.</param>
/// <param name="Show">What the lines show: <c>value</c>, <c>rate</c>, <c>p50</c>, <c>p95</c>, or <c>p99</c>.</param>
/// <param name="Split">The attribute the lines are split by; null for one line.</param>
/// <param name="SplitKeys">The attributes it can be split by.</param>
/// <param name="BucketSeconds">The width of each point.</param>
/// <param name="Lines">At most eight lines, largest latest value first.</param>
public sealed record MetricChartResponse(
    string Name,
    MetricKind Kind,
    string Unit,
    string Show,
    string? Split,
    IReadOnlyList<string> SplitKeys,
    int BucketSeconds,
    IReadOnlyList<ChartLineResponse> Lines);
