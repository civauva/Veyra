namespace Veyra.Query.ServerOverview.Api;

/// <summary>A session that waits in a blocking chain (ADR 0025).</summary>
/// <param name="SessionId">The session.</param>
/// <param name="WaitType">What it waits for, such as <c>LCK_M_U</c>.</param>
/// <param name="WaitSeconds">How long it has waited.</param>
public sealed record BlockedSessionResponse(int SessionId, string? WaitType, double? WaitSeconds);
