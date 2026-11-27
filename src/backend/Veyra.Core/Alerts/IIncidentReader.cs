namespace Veyra.Core.Alerts;

/// <summary>
/// Reads incidents (ADR 0026). The Alerting module implements it; the Query module reads the incident it gathers the
/// surrounding traces, logs, and metrics for.
/// </summary>
public interface IIncidentReader
{
    /// <summary>Reads one incident with its alerts.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The incident, or <see langword="null"/> when there is none with this ID.</returns>
    Task<IncidentState?> GetAsync(Guid id, CancellationToken cancellationToken);
}
