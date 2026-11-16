using Veyra.Query.Metrics.Api;
using Veyra.Query.Queries.Api;

namespace Veyra.Query.Server.Api;

/// <summary>A SQL Server as a whole (ADR 0020, areas 1, 2, and 14).</summary>
/// <param name="Version">Its version, such as <c>16.0.4135.4</c>.</param>
/// <param name="UpdateLevel">Its cumulative update, such as <c>CU14</c>.</param>
/// <param name="Edition">Its edition.</param>
/// <param name="UptimeSeconds">How long SQL Server has run.</param>
/// <param name="Unsupported">Whether the version is past Microsoft's end of extended support.</param>
/// <param name="SqlCpu">SQL Server's share of the CPU now, or <see langword="null"/>.</param>
/// <param name="OtherCpu">Other processes' share of the CPU now, or <see langword="null"/>.</param>
/// <param name="Runnable">Tasks waiting for a CPU now.</param>
/// <param name="Workers">The share of worker threads in use.</param>
/// <param name="MemoryTarget">The memory SQL Server aims to use.</param>
/// <param name="MemoryCommitted">The memory it uses.</param>
/// <param name="OsMemoryFree">The machine's free memory.</param>
/// <param name="OsMemoryLow">Whether the machine signals low memory.</param>
/// <param name="GrantsWaiting">Requests waiting for memory.</param>
/// <param name="DumpsDay">Memory dumps in the last day.</param>
/// <param name="AgentStopped">Whether SQL Server Agent does not run; <see langword="null"/> when there is none.</param>
/// <param name="Requests">Batch requests per second now.</param>
/// <param name="RequestsUsual">
/// Batch requests per second usual for this hour of the week, over the last four weeks.
/// </param>
/// <param name="SqlCpuLine">SQL Server's CPU over the range.</param>
/// <param name="OtherCpuLine">Other processes' CPU over the range.</param>
/// <param name="Waits">The wait types with the most time over the range, explained.</param>
/// <param name="Findings">What Veyra makes of it, with what to do.</param>
public sealed record ServerResponse(
    string Version,
    string UpdateLevel,
    string Edition,
    double? UptimeSeconds,
    bool Unsupported,
    double? SqlCpu,
    double? OtherCpu,
    double? Runnable,
    double? Workers,
    double? MemoryTarget,
    double? MemoryCommitted,
    double? OsMemoryFree,
    bool OsMemoryLow,
    double? GrantsWaiting,
    double? DumpsDay,
    bool? AgentStopped,
    double? Requests,
    double? RequestsUsual,
    ChartLineResponse SqlCpuLine,
    ChartLineResponse OtherCpuLine,
    IReadOnlyList<WaitTypeResponse> Waits,
    IReadOnlyList<PlanWarningResponse> Findings);
