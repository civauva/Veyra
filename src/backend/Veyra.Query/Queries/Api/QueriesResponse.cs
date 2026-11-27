using Veyra.Query.Systems.Api;

namespace Veyra.Query.Queries.Api;

/// <summary>A SQL Server's Queries tab (ADR 0020, area 4).</summary>
/// <param name="Range">The range, such as <c>6h</c>.</param>
/// <param name="Statements">
/// The statements, ranked by the measure asked for, at most 100 of the applications' and, when asked for, 100 of the
/// server's own; Veyra's own agent left out.
/// </param>
/// <param name="AgentStatements">How many statements of Veyra's agent ran.</param>
/// <param name="AgentSeconds">The time they took.</param>
/// <param name="QueryStore">Each user database and whether it keeps Query Store.</param>
public sealed record QueriesResponse(
    string Range,
    IReadOnlyList<QueryRowResponse> Statements,
    int AgentStatements,
    double AgentSeconds,
    IReadOnlyList<QueryStoreStateResponse> QueryStore)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }

    /// <summary>The server's own statements left out because they were not asked for.</summary>
    public HiddenRowsResponse HiddenStatements { get; init; } = HiddenRowsResponse.None;

    /// <summary>
    /// The busiest statement over the range, which the Simple view names when no statement shows a problem (VEY-67);
    /// null when nothing ran.
    /// </summary>
    public QueryRowResponse? Busiest { get; init; }

    /// <summary>The databases that do not keep Query Store, which the tab asks to switch on (VEY-68).</summary>
    public IReadOnlyList<QueryStoreStateResponse> QueryStoreOff => [.. QueryStore.Where(state => !state.On)];
}
