namespace Veyra.Query.Jobs.Api;

/// <summary>One job's screen (ADR 0027): the job, its figures, its durations, and its runs of the window.</summary>
/// <param name="From">The start of the window.</param>
/// <param name="To">The end of the window.</param>
/// <param name="Job">The job, as the Jobs list shows it.</param>
/// <param name="Figures">What its runs of the window add up to.</param>
/// <param name="Durations">Each run of the window, oldest first; at most 5,000, the newest.</param>
/// <param name="Runs">Its runs of the window, newest first; at most 200.</param>
public sealed record JobDetailResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    JobListItemResponse Job,
    JobFiguresResponse Figures,
    IReadOnlyList<JobDurationPointResponse> Durations,
    IReadOnlyList<JobRunDetailResponse> Runs);
