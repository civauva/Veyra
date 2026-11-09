namespace Veyra.Core.Telemetry;

/// <summary>
/// Persists log records. The Storage module implements it; the Ingestion module writes through it without
/// referencing Storage.
/// </summary>
public interface ILogWriter
{
    /// <summary>
    /// Stores a batch of records in one round trip.
    /// </summary>
    /// <param name="records">The records to store. The batch may be empty.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every record is stored.</returns>
    Task WriteAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken);
}
