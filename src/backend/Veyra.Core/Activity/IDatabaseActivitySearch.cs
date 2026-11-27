namespace Veyra.Core.Activity;

/// <summary>
/// Reads what database servers were doing (ADR 0020). The Storage module implements it; the Query module reads
/// through it.
/// </summary>
public interface IDatabaseActivitySearch
{
    /// <summary>Reads a server's live view as of a moment: its newest read at or before it.</summary>
    /// <param name="block">The server's block, such as <c>microsoft.sql_server sql-prod-01</c>.</param>
    /// <param name="at">The moment; the newest read at or before it is returned.</param>
    /// <param name="maxAge">How much older than the moment the read may be; an older one counts as none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The read, or <see langword="null"/> when the server sent none within the age.</returns>
    Task<SessionSnapshot?> GetSessionsAsync(
        string block,
        DateTimeOffset at,
        TimeSpan maxAge,
        CancellationToken cancellationToken);

    /// <summary>Reads the newest live view of every server that sent one within an age, for the rules.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="maxAge">How old the newest read may be.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One read per server and environment, in no particular order.</returns>
    Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken);

    /// <summary>Lists records of one kind of a server over a period, the newest first.</summary>
    /// <param name="block">The server's block.</param>
    /// <param name="kind">The kind, such as <c>sqlserver.plan</c>.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="keys">Only records with these keys, or <see langword="null"/> for every key.</param>
    /// <param name="limit">The most records.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
        string block,
        string kind,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyCollection<string>? keys,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the records of one kind of a server over a period whose attribute matches, the newest first. Storage
    /// leaves the others out before the limit, so an older match is found past many newer records (VEY-68).
    /// </summary>
    /// <param name="block">The server's block.</param>
    /// <param name="kind">The kind, such as <c>sqlserver.event</c>.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="match">Which records to keep.</param>
    /// <param name="limit">The most records.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records.</returns>
    Task<IReadOnlyList<ActivityRecord>> ListRecordsMatchingAsync(
        string block,
        string kind,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        ActivityAttributeMatch match,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the sessions the live view saw block or wait for another over a period, for blocking episodes.
    /// </summary>
    /// <param name="block">The server's block.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="limit">The most rows.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The sessions, oldest read first.</returns>
    Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
        string block,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Lists the programs and machines the live view saw run a statement over a period.</summary>
    /// <param name="block">The server's block.</param>
    /// <param name="queryHash">The statement's query hash.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The callers, the most seen first.</returns>
    Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
        string block,
        string queryHash,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
