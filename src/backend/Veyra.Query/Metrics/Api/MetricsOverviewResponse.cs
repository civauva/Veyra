namespace Veyra.Query.Metrics.Api;

/// <summary>
/// The Metrics overview: the hosts, services, database servers, and containers that sent metrics recently and match
/// its search and environment.
/// </summary>
/// <param name="At">When it was put together.</param>
/// <param name="Range">The window of the lines, for example <c>6h</c>.</param>
/// <param name="BucketSeconds">The width of each point of the lines.</param>
/// <param name="Usage">How much metric data is stored.</param>
/// <param name="Summary">The figures over the cards, counted over the cards listed (VEY-67).</param>
/// <param name="Hosts">The hosts, by name.</param>
/// <param name="Services">The services, by name and environment.</param>
/// <param name="Databases">A card per database server that sent within the last 15 minutes (ADR 0018).</param>
/// <param name="Containers">
/// A card per Docker container that sent within the last 15 minutes, by name and host (ADR 0018).
/// </param>
public sealed record MetricsOverviewResponse(
    DateTimeOffset At,
    string Range,
    int BucketSeconds,
    MetricUsageResponse Usage,
    MetricsSummaryResponse Summary,
    IReadOnlyList<HostCardResponse> Hosts,
    IReadOnlyList<ServiceCardResponse> Services,
    IReadOnlyList<DatabaseCardResponse> Databases,
    IReadOnlyList<ContainerCardResponse> Containers)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }
}
