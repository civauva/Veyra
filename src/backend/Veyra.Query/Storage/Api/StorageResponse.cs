using Veyra.Query.Queries.Api;

namespace Veyra.Query.Storage.Api;

/// <summary>A SQL Server's databases and volumes in full (ADR 0020, areas 6 to 8).</summary>
/// <param name="Databases">Its databases, those with findings first.</param>
/// <param name="Volumes">The volumes that hold their files.</param>
/// <param name="Findings">What Veyra makes of the server's storage as a whole, such as tempdb's files.</param>
public sealed record StorageResponse(
    IReadOnlyList<DatabaseStorageResponse> Databases,
    IReadOnlyList<VolumeResponse> Volumes,
    IReadOnlyList<PlanWarningResponse> Findings);
