using Veyra.Query.Queries.Api;
using Veyra.Query.Systems.Api;

namespace Veyra.Query.Storage.Api;

/// <summary>A SQL Server's databases and volumes in full (ADR 0020, areas 6 to 8).</summary>
/// <param name="Databases">
/// Its databases, those with findings first; its own left out unless asked for (VEY-66).
/// </param>
/// <param name="Volumes">The volumes that hold their files.</param>
/// <param name="Findings">What Veyra makes of the server's storage as a whole, such as tempdb's files.</param>
public sealed record StorageResponse(
    IReadOnlyList<DatabaseStorageResponse> Databases,
    IReadOnlyList<VolumeResponse> Volumes,
    IReadOnlyList<PlanWarningResponse> Findings)
{
    /// <summary>
    /// The server's own databases, left out of <see cref="Databases"/> because they were not asked for.
    /// </summary>
    public HiddenRowsResponse HiddenDatabases { get; init; } = HiddenRowsResponse.None;

    /// <summary>The findings on those databases, left out with them.</summary>
    public HiddenRowsResponse HiddenFindings { get; init; } = HiddenRowsResponse.None;

    /// <summary>
    /// The server's findings and the listed databases' in one list, the warnings first, each with its database, for
    /// the Needs doing panel (VEY-68).
    /// </summary>
    public IReadOnlyList<StorageFindingResponse> NeedsDoing { get; init; } = [];
}
