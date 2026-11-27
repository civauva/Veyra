namespace Veyra.Query.Queries;

/// <summary>What the Queries tab asks for (VEY-66): the server applies both before it keeps the top 100.</summary>
/// <param name="System">Whether the server's own statements are listed (VEY-64).</param>
/// <param name="Sort">What the statements are ranked by.</param>
/// <param name="Simple">
/// Whether only the statements with a finding are listed, as the Simple view shows them (VEY-67). The findings are
/// read for every statement before the cut to 100, so one outside the busiest is still found.
/// </param>
public sealed record QueryListing(bool System, QuerySort Sort = QuerySort.TotalTime, bool Simple = false);
