namespace Veyra.Query.Queries.Api;

/// <summary>A statement in one bucket of the range (ADR 0020, area 4).</summary>
/// <param name="Start">When the bucket starts.</param>
/// <param name="Runs">Runs in the bucket.</param>
/// <param name="PerRunSeconds">Time per run, or <see langword="null"/> without runs.</param>
/// <param name="CpuPerRunSeconds">CPU per run, or <see langword="null"/>.</param>
/// <param name="ReadsPerRun">Logical reads per run, or <see langword="null"/>.</param>
/// <param name="Plan">The plan that ran most in the bucket, or <see langword="null"/>.</param>
public sealed record QueryPointResponse(
    DateTimeOffset Start,
    long Runs,
    double? PerRunSeconds,
    double? CpuPerRunSeconds,
    double? ReadsPerRun,
    string? Plan);
