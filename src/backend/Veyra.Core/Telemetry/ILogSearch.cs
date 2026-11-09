namespace Veyra.Core.Telemetry;

/// <summary>
/// Finds stored log records. The Storage module implements it; the Query module serves it over the REST API without
/// referencing Storage.
/// </summary>
public interface ILogSearch
{
    /// <summary>
    /// Returns one page of records that match the query, newest first.
    /// </summary>
    /// <param name="query">The filters, the page size, and where the previous page ended.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The records and the cursor of the next page, if there is one.</returns>
    Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the records that match the query, per time bucket and per service.
    /// </summary>
    /// <remarks>
    /// The bucket counts honour every filter. The service counts ignore the service filter, so a person who picked
    /// one service still sees the others to add. The query's page size and cursor are not used.
    /// </remarks>
    /// <param name="query">The filters and the time window.</param>
    /// <param name="bucketSize">The width of each histogram bucket.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>The buckets and the service counts.</returns>
    Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken);
}
