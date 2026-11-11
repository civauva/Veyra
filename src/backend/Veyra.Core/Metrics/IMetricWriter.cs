namespace Veyra.Core.Metrics;

/// <summary>
/// Stores metric points. The Storage module implements it; the Ingestion module's writer calls it without referencing
/// Storage.
/// </summary>
public interface IMetricWriter
{
    /// <summary>
    /// Stores a batch of points, creating their series as needed. Points of new series beyond the series limit are
    /// dropped and logged (ADR 0010).
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many points were stored and how many were dropped.</returns>
    Task<MetricWriteResult> WriteAsync(IReadOnlyList<MetricPoint> points, CancellationToken cancellationToken);
}
