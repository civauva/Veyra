namespace Veyra.Query.Overview.Api;

/// <summary>One of Veyra's agents, as the Overview's collector health shows it (ADR 0023, point 5).</summary>
/// <param name="Name">Its name, such as "Built in".</param>
/// <param name="State">
/// Its state, named as on the Agents page: <c>NotConnected</c>, <c>Offline</c>, <c>ConfigurationFailed</c>,
/// <c>Unhealthy</c>, or <c>Online</c>.
/// </param>
/// <param name="LastSeenAt">When it last polled; null for never.</param>
/// <param name="Version">The Collector's version, once the agent has said.</param>
/// <param name="Queue">
/// The length of its exporter queue; always null until agents report it, rather than an invented number.
/// </param>
public sealed record CollectorResponse(
    string Name,
    string State,
    DateTimeOffset? LastSeenAt,
    string? Version,
    int? Queue);
