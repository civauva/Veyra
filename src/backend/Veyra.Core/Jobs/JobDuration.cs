namespace Veyra.Core.Jobs;

/// <summary>When a run started, how long it took, and how it stands: one point of a job's duration chart.</summary>
/// <param name="StartedAt">When the run started.</param>
/// <param name="DurationSeconds">How long it took; <see langword="null"/> while it goes on.</param>
/// <param name="Status">How it stands.</param>
public sealed record JobDuration(DateTimeOffset StartedAt, double? DurationSeconds, JobRunStatus Status);
