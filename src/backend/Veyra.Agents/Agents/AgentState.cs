namespace Veyra.Agents.Agents;

/// <summary>
/// How an agent stands, as the Agents page says it. The first that applies wins, in the order listed.
/// </summary>
public enum AgentState
{
    /// <summary>The agent has never connected: its container or service is not running yet.</summary>
    NotConnected,

    /// <summary>The agent has not polled for over two minutes.</summary>
    Offline,

    /// <summary>Its Collector refused the configuration Veyra sent and runs with the one before.</summary>
    ConfigurationFailed,

    /// <summary>Its Collector reports a component that does not run.</summary>
    Unhealthy,

    /// <summary>It polls and runs Veyra's configuration.</summary>
    Online,
}
