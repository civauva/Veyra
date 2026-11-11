namespace Veyra.Core.Metrics;

/// <summary>
/// Which series to find. Every filter is optional except the time the series must have been seen since.
/// </summary>
/// <param name="SeenSince">Only series with a point at or after this time.</param>
/// <param name="Names">Only these metric names.</param>
/// <param name="ServiceName">Only series of this service.</param>
/// <param name="Environment">Only series of this environment.</param>
/// <param name="HostName">Only series of this host.</param>
/// <param name="Limit">The most series to return.</param>
public sealed record MetricSeriesQuery(
    DateTimeOffset SeenSince,
    IReadOnlyList<string>? Names = null,
    string? ServiceName = null,
    string? Environment = null,
    string? HostName = null,
    int Limit = 2_000);
