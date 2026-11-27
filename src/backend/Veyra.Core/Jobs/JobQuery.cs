namespace Veyra.Core.Jobs;

/// <summary>Which jobs' runs to add up, over which window (ADR 0027).</summary>
/// <param name="From">The start of the window, inclusive.</param>
/// <param name="To">The end of the window, exclusive.</param>
/// <param name="Environment">
/// Only jobs of this environment and jobs whose sender names none, as the resource lists keep them; every environment
/// when null.
/// </param>
/// <param name="Source">Only jobs of this source; both when null.</param>
/// <param name="Search">
/// Only jobs whose name or block contains this text, in any case; every job when null or blank.
/// </param>
/// <param name="Job">Only this job; every job when null.</param>
public sealed record JobQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    string? Environment = null,
    JobSource? Source = null,
    string? Search = null,
    JobKey? Job = null);
