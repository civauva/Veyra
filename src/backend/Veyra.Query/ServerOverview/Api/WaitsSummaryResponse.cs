namespace Veyra.Query.ServerOverview.Api;

/// <summary>The time a SQL Server waited over the window, by wait type, idle waits left out (ADR 0025).</summary>
/// <param name="TotalSeconds">The seconds waited, every type together.</param>
/// <param name="Types">The five types that waited longest, then <c>Other</c> for the rest.</param>
public sealed record WaitsSummaryResponse(double TotalSeconds, IReadOnlyList<WaitTypeShareResponse> Types);
