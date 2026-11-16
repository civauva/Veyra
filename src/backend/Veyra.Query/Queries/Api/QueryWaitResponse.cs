namespace Veyra.Query.Queries.Api;

/// <summary>What a statement waited on over the range, by Query Store's wait category (ADR 0020, area 4).</summary>
/// <param name="Category">The category, such as Lock or Buffer IO.</param>
/// <param name="Seconds">How long it waited on it.</param>
public sealed record QueryWaitResponse(string Category, double Seconds);
