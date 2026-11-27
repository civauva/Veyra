namespace Veyra.Core.Alerts;

/// <summary>
/// Counts the notification channels alerts are sent through (ADR 0009), so another module can tell whether alerts
/// reach anyone, as the integration page's next steps do (ADR 0028). The Alerting module implements it.
/// </summary>
public interface INotificationChannels
{
    /// <summary>Counts the channels that are switched on.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of channels.</returns>
    Task<int> CountEnabledAsync(CancellationToken cancellationToken);
}
