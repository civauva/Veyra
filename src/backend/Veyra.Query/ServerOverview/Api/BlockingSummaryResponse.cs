namespace Veyra.Query.ServerOverview.Api;

/// <summary>A SQL Server's blocking: the chains now and blocking over the window (ADR 0025).</summary>
/// <param name="Chains">The blocking chains of the latest live read, the one with the most waiting first.</param>
/// <param name="BlockedLine">Blocked sessions per bucket of the window.</param>
/// <param name="Deadlocks">The deadlocks of the window.</param>
public sealed record BlockingSummaryResponse(
    IReadOnlyList<BlockingChainResponse> Chains,
    IReadOnlyList<double?> BlockedLine,
    int Deadlocks);
