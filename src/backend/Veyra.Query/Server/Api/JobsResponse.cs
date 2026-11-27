using Veyra.Query.Systems.Api;

namespace Veyra.Query.Server.Api;

/// <summary>A SQL Server's Jobs tab (ADR 0020, area 10).</summary>
/// <param name="Jobs">
/// The jobs, those that need a look first; those SQL Server set up itself only when asked for.
/// </param>
/// <param name="HiddenJobs">
/// The jobs SQL Server set up itself, left out because they were not asked for (VEY-66).
/// </param>
/// <param name="Enabled">
/// How many enabled jobs are listed before the Simple view keeps those that need a look, so it can tell a server
/// without jobs from one whose jobs are all well (VEY-67).
/// </param>
public sealed record JobsResponse(IReadOnlyList<JobResponse> Jobs, HiddenRowsResponse HiddenJobs, int Enabled);
