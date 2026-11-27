namespace Veyra.Query.Jobs.Api;

/// <summary>One job on the Jobs list (ADR 0027), and at the top of its own screen.</summary>
/// <param name="Id">The job's ID, opaque and safe in a URL, for <c>GET /api/v1/jobs/{id}</c>.</param>
/// <param name="Name">The job's name.</param>
/// <param name="Source"><c>trace</c> or <c>sqlagent</c>.</param>
/// <param name="Block">The block it runs on.</param>
/// <param name="Environment">Its environment, or null when the sender names none.</param>
/// <param name="LastRun">Its latest run, or null when none is known.</param>
/// <param name="Runs">How many of its runs started in the window.</param>
/// <param name="Failed">How many of them failed.</param>
/// <param name="SuccessRatio">The share of its finished runs that succeeded; null without any.</param>
/// <param name="UsualSeconds">The median duration of its runs that succeeded; null without any.</param>
/// <param name="Recent">
/// The statuses of its latest runs, at most 20, oldest first: <c>succeeded</c>, <c>failed</c>, or <c>running</c>.
/// </param>
/// <param name="Running">Whether a run goes on now.</param>
public sealed record JobListItemResponse(
    string Id,
    string Name,
    string Source,
    JobBlockResponse Block,
    string? Environment,
    JobLastRunResponse? LastRun,
    int Runs,
    int Failed,
    double? SuccessRatio,
    double? UsualSeconds,
    IReadOnlyList<string> Recent,
    bool Running);
