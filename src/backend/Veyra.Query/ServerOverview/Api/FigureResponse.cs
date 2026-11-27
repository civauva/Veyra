namespace Veyra.Query.ServerOverview.Api;

/// <summary>One figure of a SQL Server's Overview: its value now, its line over the window, and its change.</summary>
/// <param name="Value">The value now, at the end of the window; null without data.</param>
/// <param name="Line">One value per bucket of the window, the oldest first; null where nothing was measured.</param>
/// <param name="Change">
/// The window's average against the equally long period before (ADR 0022): a share of what it was for amounts, a
/// difference for ratios; null without a period before.
/// </param>
public sealed record FigureResponse(double? Value, IReadOnlyList<double?> Line, double? Change);
