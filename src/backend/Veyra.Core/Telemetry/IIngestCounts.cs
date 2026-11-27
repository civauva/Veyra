namespace Veyra.Core.Telemetry;

/// <summary>
/// Reads how many log records, spans, and metric points were stored per minute (ADR 0023). The Storage module keeps
/// the counts as it writes each batch; the Query module draws the Overview's ingestion from them.
/// </summary>
public interface IIngestCounts
{
    /// <summary>Adds up the counts of a window per step and signal.</summary>
    /// <param name="environment">Only telemetry that names this environment; every environment when null.</param>
    /// <param name="periodStart">The start of the window, on a step boundary.</param>
    /// <param name="periodEnd">The end of the window; a minute it ends inside is counted whole.</param>
    /// <param name="bucket">The width of a step, a whole number of minutes.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One count per step and signal that had any, in no particular order.</returns>
    Task<IReadOnlyList<IngestCount>> CountAsync(
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        TimeSpan bucket,
        CancellationToken cancellationToken);
}
