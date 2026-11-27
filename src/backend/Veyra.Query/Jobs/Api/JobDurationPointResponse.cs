namespace Veyra.Query.Jobs.Api;

/// <summary>One run on a job's duration chart.</summary>
/// <param name="At">When the run started.</param>
/// <param name="Seconds">How long it took; null while it goes on.</param>
/// <param name="Status"><c>succeeded</c>, <c>failed</c>, or <c>running</c>.</param>
public sealed record JobDurationPointResponse(DateTimeOffset At, double? Seconds, string Status);
