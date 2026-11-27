using Veyra.Core.Telemetry;

namespace Veyra.Storage.Ingest;

/// <summary>
/// Stands in for <see cref="IngestCountStore"/> when no connection string is configured, so Veyra can start and report
/// the problem through <c>/readyz</c> instead of failing at startup.
/// </summary>
internal sealed class UnconfiguredIngestCounts : IIngestCounts
{
    /// <summary>Always fails: there is no database to count in.</summary>
    /// <param name="environment">Ignored.</param>
    /// <param name="periodStart">Ignored.</param>
    /// <param name="periodEnd">Ignored.</param>
    /// <param name="bucket">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<IngestCount>> CountAsync(
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        TimeSpan bucket,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
