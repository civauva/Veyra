namespace Veyra.Query.Metrics.Api;

/// <summary>
/// What Veyra's agent itself asked of the server over the range: its statements are kept out of the top queries, so
/// the server's own work is what the list shows, and summed up here instead.
/// </summary>
/// <param name="Statements">How many different statements of the agent ran.</param>
/// <param name="TotalSeconds">Their total elapsed time.</param>
public sealed record AgentWorkResponse(int Statements, double TotalSeconds);
