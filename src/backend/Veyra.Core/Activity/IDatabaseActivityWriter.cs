namespace Veyra.Core.Activity;

/// <summary>
/// Persists what database servers are doing (ADR 0020). The Storage module implements it; the Ingestion module writes
/// through it without referencing Storage.
/// </summary>
public interface IDatabaseActivityWriter
{
    /// <summary>Stores a batch of records in one round trip.</summary>
    /// <param name="records">The records, of any kind. The batch may be empty.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when every record is stored.</returns>
    Task WriteAsync(IReadOnlyList<DatabaseActivity> records, CancellationToken cancellationToken);
}
