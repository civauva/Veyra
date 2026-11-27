namespace Veyra.Query.ServerOverview.Api;

/// <summary>A blocking chain: the session at its head and every session that waits behind it (ADR 0025).</summary>
/// <param name="HeadSessionId">The head blocker, which waits for nobody.</param>
/// <param name="Database">The head's database.</param>
/// <param name="Login">The head's login.</param>
/// <param name="Host">The machine the head connected from.</param>
/// <param name="WaitType">What the head waits for, if anything.</param>
/// <param name="WaitSeconds">How long the head has waited.</param>
/// <param name="BlockedCount">How many sessions wait behind it, directly or through others.</param>
/// <param name="Statement">The head's statement, or the last one it ran.</param>
/// <param name="Blocked">The sessions that wait behind it, the nearest first.</param>
public sealed record BlockingChainResponse(
    int HeadSessionId,
    string? Database,
    string? Login,
    string? Host,
    string? WaitType,
    double? WaitSeconds,
    int BlockedCount,
    string? Statement,
    IReadOnlyList<BlockedSessionResponse> Blocked);
