namespace Veyra.Core.Metrics;

/// <summary>
/// One received metric point with everything that identifies its series. Ingestion produces it from OTLP; the Storage
/// module splits it into a series and a point (ADR 0010).
/// </summary>
/// <param name="Name">The metric name, for example <c>system.cpu.time</c>.</param>
/// <param name="Kind">Gauge, sum, or histogram.</param>
/// <param name="Unit">The unit as sent, for example <c>s</c> or <c>By</c>; empty when none was sent.</param>
/// <param name="Description">The description as sent; empty when none was sent.</param>
/// <param name="Temporality">Delta or cumulative for sums and histograms; unspecified for gauges.</param>
/// <param name="Monotonic">Whether a sum only ever grows. Always false for gauges and histograms.</param>
/// <param name="ServiceName">
/// From <c>service.name</c>; null when the sender set none, as Collector host metrics do.
/// </param>
/// <param name="Environment">From <c>deployment.environment.name</c>; null when none was set.</param>
/// <param name="HostName">From <c>host.name</c>; null when none was set.</param>
/// <param name="ResourceAttributes">The resource's attributes as text.</param>
/// <param name="Attributes">The point's own attributes as text, for example <c>state=user</c>.</param>
/// <param name="Timestamp">When the point was measured.</param>
/// <param name="StartTimestamp">For cumulative points, when counting started; null when not sent.</param>
/// <param name="Value">The value of a gauge or sum; null for histograms.</param>
/// <param name="Histogram">The distribution of a histogram point; null otherwise.</param>
public sealed record MetricPoint(
    string Name,
    MetricKind Kind,
    string Unit,
    string Description,
    MetricTemporality Temporality,
    bool Monotonic,
    string? ServiceName,
    string? Environment,
    string? HostName,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyDictionary<string, string> Attributes,
    DateTimeOffset Timestamp,
    DateTimeOffset? StartTimestamp,
    double? Value,
    HistogramValue? Histogram);
