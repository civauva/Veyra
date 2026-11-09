namespace Veyra.Query;

/// <summary>
/// Settings of the Query module, bound from the <c>Veyra:Query</c> section.
/// </summary>
public sealed class QueryOptions
{
    /// <summary>Upper bound on the number of records a single query page may return.</summary>
    public int MaxPageSize { get; set; } = 1_000;
}
