namespace Veyra.Query.Blocking.Api;

/// <summary>A SQL Server's Blocking tab over the range (ADR 0020, area 5).</summary>
/// <param name="Range">The range.</param>
/// <param name="Episodes">The blocking episodes, the newest first.</param>
/// <param name="Deadlocks">The deadlocks, the newest first.</param>
public sealed record BlockingResponse(
    string Range,
    IReadOnlyList<EpisodeResponse> Episodes,
    IReadOnlyList<DeadlockResponse> Deadlocks)
{
    /// <summary>The start of the period the answer covers (ADR 0022).</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>The end of the period the answer covers: now, or the <c>to</c> asked for (ADR 0022).</summary>
    public DateTimeOffset To { get; init; }
}
