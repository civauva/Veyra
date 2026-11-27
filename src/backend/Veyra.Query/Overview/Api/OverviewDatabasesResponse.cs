namespace Veyra.Query.Overview.Api;

/// <summary>One page of the databases of every server, worst first (ADR 0023, point 6).</summary>
/// <param name="Items">At most twelve databases.</param>
/// <param name="Total">How many databases every server has, the servers' own left out.</param>
public sealed record OverviewDatabasesResponse(IReadOnlyList<OverviewDatabaseResponse> Items, int Total);
