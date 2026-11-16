namespace Veyra.Agents.Api;

/// <summary>An agent for another network, as the Agents page adds it.</summary>
/// <param name="Name">What the page calls it, such as "Company network": 1 to 60 characters.</param>
public sealed record AddAgentRequest(string Name);
