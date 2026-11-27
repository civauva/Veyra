namespace Veyra.Query.ServerOverview.Api;

/// <summary>The free share of the fullest volume that holds database files (ADR 0025).</summary>
/// <param name="Value">Its free share now, 0 to 1; null without volumes.</param>
/// <param name="Line">The fullest volume's free share per bucket of the window, whichever volume it was.</param>
/// <param name="Change">The window's average against the period before, as a difference of shares.</param>
/// <param name="Volume">The fullest volume now, such as <c>D:\</c>.</param>
public sealed record VolumeFigureResponse(double? Value, IReadOnlyList<double?> Line, double? Change, string? Volume);
