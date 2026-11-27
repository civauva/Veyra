namespace Veyra.Query.Overview.Api;

/// <summary>How the SQL Server Agent job runs of the window went, across every server (ADR 0023, point 4).</summary>
/// <param name="SucceededRatio">The runs that succeeded, out of all runs, from 0 to 1; null without runs.</param>
/// <param name="Runs">How many runs started in the window.</param>
/// <param name="Failed">How many of them did not succeed.</param>
public sealed record JobsFigureResponse(double? SucceededRatio, int Runs, int Failed);
