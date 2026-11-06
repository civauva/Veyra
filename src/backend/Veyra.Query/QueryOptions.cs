namespace Veyra.Query;

public sealed class QueryOptions
{
    /// <summary>Upper bound on the number of records a single query page may return.</summary>
    public int MaxPageSize { get; set; } = 1_000;
}
