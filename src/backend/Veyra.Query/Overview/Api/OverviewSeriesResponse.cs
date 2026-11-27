namespace Veyra.Query.Overview.Api;

/// <summary>A panel's line over the window, one point per step the server chose (ADR 0022).</summary>
/// <typeparam name="TPoint">What each point holds.</typeparam>
/// <param name="StepSeconds">The width of a step, in seconds.</param>
/// <param name="Points">One point per step, oldest first.</param>
public sealed record OverviewSeriesResponse<TPoint>(int StepSeconds, IReadOnlyList<TPoint> Points);
