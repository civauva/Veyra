namespace Veyra.Core.Maintenance;

/// <summary>
/// Deletes the telemetry one module keeps, for an Admin's "Clear all telemetry" on the Settings screen (VEY-59). Each
/// module that stores telemetry registers one. Users, settings, agents, monitored servers, ingestion keys, and
/// notification channels are not telemetry and stay.
/// </summary>
public interface ITelemetryClearer
{
    /// <summary>Deletes everything the module keeps of its telemetry; ingestion carries on afterwards.</summary>
    /// <param name="cancellationToken">
    /// Cancels the clear before it starts; once started, it runs in one transaction.
    /// </param>
    /// <returns>A task that completes when the telemetry is gone.</returns>
    Task ClearAsync(CancellationToken cancellationToken);
}
