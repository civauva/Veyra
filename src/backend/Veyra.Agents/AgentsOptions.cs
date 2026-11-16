namespace Veyra.Agents;

/// <summary>
/// Settings of the Agents module, bound from <c>Veyra:Agents</c>. Both say where Veyra's Docker Compose deployment
/// puts the built-in agent, so they are set by <c>deploy/docker-compose.yml</c> and nobody changes them.
/// </summary>
public sealed class AgentsOptions
{
    /// <summary>
    /// The folder, shared with the built-in agent's container, where Veyra writes that agent's start-up file with its
    /// key. Empty means there is no built-in agent, as when Veyra runs outside Docker Compose.
    /// </summary>
    public string? BuiltInDirectory { get; set; }

    /// <summary>The address at which the built-in agent reaches Veyra, inside the Compose network.</summary>
    public string BuiltInServerUrl { get; set; } = "http://veyra:8080";
}
