namespace Veyra.Query.Overview.Api;

/// <summary>
/// The share of time in which no service had a warning open, read from the alert history (ADR 0023, point 3). It is
/// not an uptime probe: Veyra does not probe services.
/// </summary>
/// <param name="Ratio">The share of the last <paramref name="Days"/> days, from 0 to 1; null with no services.</param>
/// <param name="Change">
/// The change against the days before, in percentage points as a ratio; null with no services.
/// </param>
/// <param name="Days">How many days the share covers, ending at the window's end.</param>
public sealed record AvailabilityFigureResponse(double? Ratio, double? Change, int Days);
