namespace Veyra.Agents.Api;

/// <summary>The Collector that reads an integration, from its agent's OpAMP reports (ADR 0028).</summary>
/// <param name="AgentId">The agent's ID.</param>
/// <param name="AgentName">Its name.</param>
/// <param name="Healthy">Whether the Collector says it runs well; <see langword="null"/> until it says.</param>
/// <param name="StartedAt">When the Collector last started, once it has said.</param>
/// <param name="UptimeSeconds">How long it has run since, in seconds.</param>
/// <param name="LastSeenAt">When the agent last polled.</param>
/// <param name="ConfigState">
/// What the agent said of its configuration: <c>applied</c>, <c>applying</c>, <c>failed</c>, or <c>unknown</c>.
/// </param>
/// <param name="ConfigError">Why the Collector refused it, when it did.</param>
public sealed record CollectorResponse(
    Guid AgentId,
    string AgentName,
    bool? Healthy,
    DateTimeOffset? StartedAt,
    double? UptimeSeconds,
    DateTimeOffset? LastSeenAt,
    string ConfigState,
    string? ConfigError);
