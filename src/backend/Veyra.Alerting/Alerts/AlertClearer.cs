using Npgsql;
using Veyra.Core.Maintenance;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Deletes every alert, its events, the deliveries still waiting to be sent (VEY-59), and the incidents that group
/// them (ADR 0026). The rules read open alerts from the database on every run, so an alert whose condition still holds
/// opens again on the next one, in a new incident.
/// </summary>
/// <param name="dataSource">The database.</param>
internal sealed class AlertClearer(NpgsqlDataSource dataSource) : ITelemetryClearer
{
    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "TRUNCATE alerting.alert, alerting.alert_event, alerting.delivery, alerting.incident, "
            + "alerting.incident_alert, alerting.incident_event");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
