namespace Veyra.Agents.Api;

/// <summary>The agent that reads an integration.</summary>
/// <param name="Id">The agent's ID.</param>
/// <param name="Name">Its name, such as "Built in".</param>
public sealed record IntegrationAgentResponse(Guid Id, string Name);
