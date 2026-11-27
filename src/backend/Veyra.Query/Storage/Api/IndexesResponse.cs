using Veyra.Query.Queries.Api;
using Veyra.Query.Systems.Api;

namespace Veyra.Query.Storage.Api;

/// <summary>A SQL Server's index and statistics advice (ADR 0020, area 9).</summary>
/// <param name="Findings">Missing, unused, and duplicate indexes, heaps, stale statistics, and escalations.</param>
/// <param name="Indexes">The 100 largest indexes with their use.</param>
public sealed record IndexesResponse(IReadOnlyList<PlanWarningResponse> Findings, IReadOnlyList<IndexResponse> Indexes)
{
    /// <summary>
    /// The indexes of the server's own databases, left out of <see cref="Indexes"/> and of the findings because they
    /// were not asked for (VEY-66).
    /// </summary>
    public HiddenRowsResponse HiddenIndexes { get; init; } = HiddenRowsResponse.None;
}
