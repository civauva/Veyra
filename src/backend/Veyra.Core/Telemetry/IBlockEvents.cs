namespace Veyra.Core.Telemetry;

/// <summary>
/// Reads the recent events of one block, such as a database server's query samples and top queries (ADR 0018).
/// </summary>
public interface IBlockEvents
{
    /// <summary>Lists a block's newest events of one name.</summary>
    /// <param name="block">The block.</param>
    /// <param name="eventName">The event name, for example <c>db.server.query_sample</c>.</param>
    /// <param name="since">Only events at or after this time.</param>
    /// <param name="until">Only events before this time, usually now.</param>
    /// <param name="limit">The most events to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The events, newest first.</returns>
    Task<IReadOnlyList<LogRecord>> ListAsync(
        TelemetryBlock block,
        string eventName,
        DateTimeOffset since,
        DateTimeOffset until,
        int limit,
        CancellationToken cancellationToken);
}
