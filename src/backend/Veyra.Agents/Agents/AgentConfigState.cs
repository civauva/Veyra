namespace Veyra.Agents.Agents;

/// <summary>
/// What an agent last said about the configuration Veyra sent it. The numbers are OpAMP's
/// <c>RemoteConfigStatuses</c>, and are stored as they are.
/// </summary>
public enum AgentConfigState
{
    /// <summary>The agent has not said yet.</summary>
    Unknown = 0,

    /// <summary>The agent runs with it.</summary>
    Applied = 1,

    /// <summary>The agent is starting its Collector with it.</summary>
    Applying = 2,

    /// <summary>The Collector refused it; the agent keeps running with its previous configuration.</summary>
    Failed = 3,
}
