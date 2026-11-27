namespace Veyra.Query;

/// <summary>
/// Settings of the Query module, bound from the <c>Veyra:Query</c> section.
/// </summary>
public sealed class QueryOptions
{
    /// <summary>Upper bound on the number of records a single query page may return.</summary>
    public int MaxPageSize { get; set; } = 1_000;

    /// <summary>
    /// How long a host, container, or database server may send nothing before the resource lists show it as silent
    /// (ADR 0022). The Collector sends every minute, so a few missed sends are enough to tell.
    /// </summary>
    public TimeSpan SilentAfterHosts { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a service may send nothing before the resource lists show it as silent (ADR 0022). Longer than for
    /// hosts, because a service's SDK only sends while it runs, and a batch job runs now and then.
    /// </summary>
    public TimeSpan SilentAfterServices { get; set; } = TimeSpan.FromHours(2);

    /// <summary>
    /// The most live streams one Veyra instance serves at once, <c>/api/v1/live</c> and <c>/api/v1/logs/stream</c>
    /// together (ADR 0022). Each holds a connection open and, for logs, a queue of up to 1,000 records.
    /// </summary>
    public int LiveListeners { get; set; } = 50;

    /// <summary>
    /// The most records one export of <c>/api/v1/logs/export</c> writes (ADR 0024). An export reads page by page, so
    /// memory stays flat, but it holds a connection and a database session for as long as it runs; the limit bounds
    /// that time.
    /// </summary>
    public int MaxExportRecords { get; set; } = 100_000;
}
