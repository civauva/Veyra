namespace Veyra.Query.Queries.Api;

/// <summary>One plan a statement used over the range (ADR 0020, area 4).</summary>
/// <param name="Hash">The plan hash.</param>
/// <param name="Runs">Runs on it over the range.</param>
/// <param name="PerRunSeconds">Time per run on it.</param>
/// <param name="CpuPerRunSeconds">CPU per run on it.</param>
/// <param name="ReadsPerRun">Logical reads per run on it.</param>
/// <param name="FirstSeen">When the agent first reported it.</param>
/// <param name="LastSeen">When the agent last reported it.</param>
/// <param name="Captured">Whether Veyra holds its XML, to show and to download.</param>
/// <param name="Operators">Its operators, in plan order, when captured.</param>
/// <param name="Warnings">Its warnings, when captured.</param>
public sealed record QueryPlanResponse(
    string Hash,
    long Runs,
    double PerRunSeconds,
    double CpuPerRunSeconds,
    double ReadsPerRun,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    bool Captured,
    IReadOnlyList<PlanOperatorResponse> Operators,
    IReadOnlyList<PlanWarningResponse> Warnings);
