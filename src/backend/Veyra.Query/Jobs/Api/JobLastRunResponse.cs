namespace Veyra.Query.Jobs.Api;

/// <summary>A job's latest run.</summary>
/// <param name="StartedAt">When it started.</param>
/// <param name="DurationSeconds">How long it took; null while it goes on.</param>
/// <param name="Status"><c>succeeded</c>, <c>failed</c>, or <c>running</c>.</param>
public sealed record JobLastRunResponse(DateTimeOffset StartedAt, double? DurationSeconds, string Status);
