using Veyra.Agents.Agents;

namespace Veyra.Agents.Enrollment;

/// <summary>An agent just added on the Agents page, with the code that installs it.</summary>
/// <param name="Agent">The agent, not installed yet.</param>
/// <param name="Code">Its one-time code. Only its hash is stored, so it is shown this once.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
internal sealed record AddedAgent(Agent Agent, string Code, DateTimeOffset ExpiresAt);
