namespace Veyra.Agents.Agents;

/// <summary>
/// What an agent last reported over OpAMP. OpAMP sends only what changed, so each report is merged into the last.
/// </summary>
/// <param name="InstanceUid">The agent's OpAMP instance ID. A new one means the agent started afresh.</param>
/// <param name="Version">The Collector's version, such as <c>0.140.1</c>.</param>
/// <param name="HostName">The machine or container it runs on.</param>
/// <param name="OperatingSystem">Its operating system, such as <c>Alpine 3.22.2</c>.</param>
/// <param name="Healthy">Whether the Collector says it runs well; <see langword="null"/> until it says.</param>
/// <param name="HealthError">The Collector's last error, when it has one.</param>
/// <param name="ConfigHash">The hash of the configuration the agent last reported on.</param>
/// <param name="ConfigState">What it said about that configuration.</param>
/// <param name="ConfigError">Why the Collector refused it, when it did.</param>
/// <param name="LastSeenAt">When the agent last connected.</param>
internal sealed record AgentReport(
    byte[]? InstanceUid,
    string? Version,
    string? HostName,
    string? OperatingSystem,
    bool? Healthy,
    string? HealthError,
    byte[]? ConfigHash,
    AgentConfigState ConfigState,
    string? ConfigError,
    DateTimeOffset? LastSeenAt)
{
    /// <summary>The report of an agent that has never connected.</summary>
    public static AgentReport None { get; } =
        new(null, null, null, null, null, null, null, AgentConfigState.Unknown, null, null);
}
