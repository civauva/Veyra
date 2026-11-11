namespace Veyra.Core.Alerts;

/// <summary>
/// Reads alerts. The Alerting module implements it; the Query module sets block health from it, so health always
/// has an alert behind it (ADR 0007).
/// </summary>
public interface IAlertStates
{
    /// <summary>
    /// Lists every open alert and the alerts that resolved at or after the given time.
    /// </summary>
    /// <param name="resolvedSince">The oldest resolution time to include.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The alerts, in no particular order.</returns>
    Task<IReadOnlyList<AlertState>> ListAsync(DateTimeOffset resolvedSince, CancellationToken cancellationToken);
}
