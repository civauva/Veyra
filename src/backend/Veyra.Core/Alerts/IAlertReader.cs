namespace Veyra.Core.Alerts;

/// <summary>
/// Reads one alert (ADR 0029). The Alerting module implements it; the Query module reads the alert it gathers the
/// surrounding blocks and logs for, as it reads incidents through <see cref="IIncidentReader"/>.
/// </summary>
public interface IAlertReader
{
    /// <summary>Reads one alert, open or resolved.</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The alert, or <see langword="null"/> when there is none with this ID.</returns>
    Task<AlertState?> GetAsync(Guid id, CancellationToken cancellationToken);
}
