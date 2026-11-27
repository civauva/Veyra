using Veyra.Query.Home;

namespace Veyra.Query.Resources.Api;

/// <summary>One service of the list: every service whose metrics are kept, silent ones too (ADR 0022).</summary>
/// <param name="Name">From <c>service.name</c>.</param>
/// <param name="Environment">From <c>deployment.environment.name</c>, if set.</param>
/// <param name="Health">From its open alerts only (ADR 0007).</param>
/// <param name="Silent">Whether it sent no metrics for longer than <c>Veyra:Query:SilentAfterServices</c>.</param>
/// <param name="RequestsPerMinute">
/// HTTP requests served per minute over the window; null when it sends no <c>http.server.request.duration</c>.
/// </param>
/// <param name="P95Seconds">The 95th percentile of their duration over the window.</param>
/// <param name="ErrorRatio">The share answered with a 5xx status over the window, from 0 to 1.</param>
/// <param name="Instances">How many instances sent metrics, by <c>service.instance.id</c>.</param>
/// <param name="Hosts">The hosts it runs on, from <c>host.name</c> on its resources, alphabetically.</param>
/// <param name="LastSeenAt">When its metrics last arrived, to within a minute.</param>
/// <param name="Requests">
/// Requests per minute in each of 24 equal slices of the window, oldest first, for the small line; null where
/// nothing was measured.
/// </param>
public sealed record ServiceItemResponse(
    string Name,
    string? Environment,
    BlockHealth Health,
    bool Silent,
    double? RequestsPerMinute,
    double? P95Seconds,
    double? ErrorRatio,
    int Instances,
    IReadOnlyList<string> Hosts,
    DateTimeOffset LastSeenAt,
    IReadOnlyList<double?> Requests);
