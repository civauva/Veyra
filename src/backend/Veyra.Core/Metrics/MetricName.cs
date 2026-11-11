namespace Veyra.Core.Metrics;

/// <summary>One metric name and what is stored under it, for browsing.</summary>
/// <param name="Name">The metric name.</param>
/// <param name="Kind">Gauge, sum, or histogram.</param>
/// <param name="Unit">The unit as sent.</param>
/// <param name="Description">The description as sent.</param>
/// <param name="Series">How many series have the name.</param>
/// <param name="Sources">The services or hosts that send it, most series first, at most five.</param>
public sealed record MetricName(
    string Name,
    MetricKind Kind,
    string Unit,
    string Description,
    int Series,
    IReadOnlyList<string> Sources);
