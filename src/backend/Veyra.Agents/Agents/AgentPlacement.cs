namespace Veyra.Agents.Agents;

/// <summary>Where an agent runs. Stored as its number.</summary>
public enum AgentPlacement
{
    /// <summary>Beside Veyra in its Docker Compose deployment, set up by Veyra itself. There is at most one.</summary>
    BuiltIn = 1,

    /// <summary>Installed in another network, for systems Veyra cannot reach.</summary>
    Remote = 2,
}
