namespace Veyra.Query.Jobs.Api;

/// <summary>The Jobs list (ADR 0027): one row per job, those whose latest run failed first.</summary>
/// <param name="From">The start of the window.</param>
/// <param name="To">The end of the window.</param>
/// <param name="Figures">
/// What the jobs add up to before the status filter, so the screen's status tabs keep their numbers.
/// </param>
/// <param name="Items">The jobs.</param>
public sealed record JobListResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    JobListFiguresResponse Figures,
    IReadOnlyList<JobListItemResponse> Items);
