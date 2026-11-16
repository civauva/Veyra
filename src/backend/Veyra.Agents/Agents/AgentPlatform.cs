namespace Veyra.Agents.Agents;

/// <summary>Where an agent's programs live, which decides the paths in its start-up file.</summary>
public enum AgentPlatform
{
    /// <summary>The built-in agent's container, from <c>deploy/agent/Dockerfile</c>.</summary>
    Container,

    /// <summary>A Windows service installed by Veyra's PowerShell installer.</summary>
    Windows,

    /// <summary>A systemd service installed by Veyra's shell installer.</summary>
    Linux,
}
