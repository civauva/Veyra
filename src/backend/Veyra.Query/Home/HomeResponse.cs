namespace Veyra.Query.Home;

/// <summary>
/// The home snapshot: everything a home dashboard draws, in one object (ADR 0007).
/// </summary>
/// <param name="Environment">The environment shown, or null for all environments together.</param>
/// <param name="At">When the snapshot was taken.</param>
/// <param name="Environments">Every environment seen in the last hour or with an open alert, by name.</param>
/// <param name="Condition">The weather of the environment shown.</param>
/// <param name="Counts">How many blocks are in each state.</param>
/// <param name="Alerts">The open alerts, numbered: warnings first, then oldest first.</param>
/// <param name="Resolved">The alerts resolved in the last 24 hours, newest first.</param>
/// <param name="Blocks">Every service seen in the last hour or with an open alert, by name.</param>
/// <param name="Metrics">The key metrics.</param>
/// <param name="Hourly">The hourly strip.</param>
/// <param name="Storage">Log storage and its outlook.</param>
/// <param name="Disks">Every disk watched through host metrics: those that fill soonest first, then the fullest.</param>
public sealed record HomeResponse(
    string? Environment,
    DateTimeOffset At,
    IReadOnlyList<HomeEnvironmentResponse> Environments,
    HomeConditionResponse Condition,
    HealthCountsResponse Counts,
    IReadOnlyList<HomeAlertResponse> Alerts,
    IReadOnlyList<HomeAlertResponse> Resolved,
    IReadOnlyList<BlockResponse> Blocks,
    HomeMetricsResponse Metrics,
    HourlyResponse Hourly,
    LogStorageResponse Storage,
    IReadOnlyList<HomeDiskResponse> Disks);
