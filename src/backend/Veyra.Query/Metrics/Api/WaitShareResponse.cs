namespace Veyra.Query.Metrics.Api;

/// <summary>The wait that took the most time over a range (VEY-68).</summary>
/// <param name="Label">The wait's category, such as <c>CPU</c>.</param>
/// <param name="Share">Its share of all the time waited, from 0 to 1.</param>
public sealed record WaitShareResponse(string Label, double Share);
