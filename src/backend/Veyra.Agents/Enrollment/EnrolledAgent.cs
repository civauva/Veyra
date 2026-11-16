namespace Veyra.Agents.Enrollment;

/// <summary>What an installer gets for a good code: the agent's name and its start-up file.</summary>
/// <param name="AgentName">The agent's name, for the installer to show.</param>
/// <param name="SupervisorConfig">
/// The OpAMP supervisor's start-up file, with the agent's key. The installer writes it where only the system and
/// administrators can read it.
/// </param>
public sealed record EnrolledAgent(string AgentName, string SupervisorConfig)
{
    /// <summary>Keeps the key out of logs and debugger views, which print records by their members.</summary>
    /// <returns>The agent's name only.</returns>
    public override string ToString() => $"EnrolledAgent {{ AgentName = {AgentName} }}";
}
