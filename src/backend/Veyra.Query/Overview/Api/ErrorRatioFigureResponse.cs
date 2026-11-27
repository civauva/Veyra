namespace Veyra.Query.Overview.Api;

/// <summary>The share of the window's HTTP requests answered with a 5xx status.</summary>
/// <param name="Value">The share, from 0 to 1; null without requests.</param>
/// <param name="Change">
/// The change against the period before, in its own unit: 0.0012 is 0.12 percentage points; null when either period
/// has no requests.
/// </param>
public sealed record ErrorRatioFigureResponse(double? Value, double? Change);
