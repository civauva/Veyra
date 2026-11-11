namespace Veyra.Core.Metrics;

/// <summary>One stored series: a metric name with one exact set of attributes (ADR 0010).</summary>
/// <param name="Id">The series' ID.</param>
/// <param name="Name">The metric name.</param>
/// <param name="Kind">Gauge, sum, or histogram.</param>
/// <param name="Unit">The unit as sent.</param>
/// <param name="Description">The description as sent.</param>
/// <param name="Temporality">Delta or cumulative; unspecified for gauges.</param>
/// <param name="Monotonic">Whether a sum only ever grows.</param>
/// <param name="ServiceName">The sending service, if any.</param>
/// <param name="Environment">The environment, if any.</param>
/// <param name="HostName">The host, if any.</param>
/// <param name="ResourceAttributes">The resource's attributes.</param>
/// <param name="Attributes">The series' own attributes.</param>
/// <param name="FirstSeen">When its first point arrived.</param>
/// <param name="LastSeen">When a point last arrived, to within a minute.</param>
public sealed record MetricSeries(
    long Id,
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
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);
