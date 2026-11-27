namespace Veyra.Core.Agents;

/// <summary>How one of Veyra's agents stands, as the Agents page tells it (ADR 0019).</summary>
/// <param name="Name">The agent's name, such as "Built in".</param>
/// <param name="State">
/// Its state, named as on the Agents page: <c>NotConnected</c>, <c>Offline</c>, <c>ConfigurationFailed</c>,
/// <c>Unhealthy</c>, or <c>Online</c>.
/// </param>
/// <param name="LastSeenAt">When it last polled; <see langword="null"/> for never.</param>
/// <param name="Version">The Collector's version, once the agent has said.</param>
public sealed record AgentStatus(string Name, string State, DateTimeOffset? LastSeenAt, string? Version);
