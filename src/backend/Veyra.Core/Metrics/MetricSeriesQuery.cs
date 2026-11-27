using Veyra.Core.Telemetry;

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
/// <param name="Block">Only series of this block, of any kind (ADR 0018).</param>
/// <param name="BlockNameContains">
/// Only series whose block name holds this text in any case, as a search on the Metrics overview asks (VEY-67). It is
/// applied before <paramref name="Limit"/>, so a match is found among any number of series.
/// </param>
/// <param name="OrNoEnvironment">
/// With <paramref name="Environment"/>, also series that name no environment: a host, database server, or container
/// without one belongs to every environment.
/// </param>
public sealed record MetricSeriesQuery(
    DateTimeOffset SeenSince,
    IReadOnlyList<string>? Names = null,
    string? ServiceName = null,
    string? Environment = null,
    string? HostName = null,
    int Limit = 2_000,
    TelemetryBlock? Block = null,
    string? BlockNameContains = null,
    bool OrNoEnvironment = false);
