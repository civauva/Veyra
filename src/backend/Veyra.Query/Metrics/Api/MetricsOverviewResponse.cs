namespace Veyra.Query.Metrics.Api;

/// <summary>The Metrics overview: every host and service that sent metrics recently.</summary>
/// <param name="At">When it was put together.</param>
/// <param name="Range">The window of the lines, for example <c>6h</c>.</param>
/// <param name="BucketSeconds">The width of each point of the lines.</param>
/// <param name="Usage">How much metric data is stored.</param>
/// <param name="Hosts">The hosts, by name.</param>
/// <param name="Services">The services, by name and environment.</param>
public sealed record MetricsOverviewResponse(
    DateTimeOffset At,
    string Range,
    int BucketSeconds,
    MetricUsageResponse Usage,
    IReadOnlyList<HostCardResponse> Hosts,
    IReadOnlyList<ServiceCardResponse> Services);
