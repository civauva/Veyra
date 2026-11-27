namespace Veyra.Core.Jobs;

/// <summary>
/// One step of a job run (ADR 0027): a direct child of a traced run's root span, or a step of a SQL Agent job.
/// </summary>
/// <param name="Name">The step's name.</param>
/// <param name="DurationSeconds">How long it took, or <see langword="null"/> when that is not known.</param>
/// <param name="Status">
/// How it ended, or <see langword="null"/> when the sender said something Veyra cannot read.
/// </param>
/// <param name="Message">What it said about how it ended, such as an error, or <see langword="null"/>.</param>
public sealed record JobStep(string Name, double? DurationSeconds, JobRunStatus? Status, string? Message);
