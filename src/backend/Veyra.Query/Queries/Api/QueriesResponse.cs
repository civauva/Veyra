namespace Veyra.Query.Queries.Api;

/// <summary>A SQL Server's Queries tab (ADR 0020, area 4).</summary>
/// <param name="Range">The range, such as <c>6h</c>.</param>
/// <param name="Statements">The statements, by total time, at most 100; Veyra's own agent left out.</param>
/// <param name="AgentStatements">How many statements of Veyra's agent ran.</param>
/// <param name="AgentSeconds">The time they took.</param>
/// <param name="QueryStore">Each user database and whether it keeps Query Store.</param>
public sealed record QueriesResponse(
    string Range,
    IReadOnlyList<QueryRowResponse> Statements,
    int AgentStatements,
    double AgentSeconds,
    IReadOnlyList<QueryStoreStateResponse> QueryStore);
