namespace Veyra.Agents.Api;

/// <summary>An agent just added, with the one-time code that installs it.</summary>
/// <param name="Agent">The agent, not installed yet.</param>
/// <param name="Code">The code. Only its hash is kept, so this is the one time it is shown.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
public sealed record AddedAgentResponse(AgentResponse Agent, string Code, DateTimeOffset ExpiresAt);
