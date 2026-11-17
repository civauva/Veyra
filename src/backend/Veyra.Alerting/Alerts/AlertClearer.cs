using Npgsql;
using Veyra.Core.Maintenance;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Deletes every alert, its events, and the deliveries still waiting to be sent (VEY-59). The rules read open alerts
/// from the database on every run, so an alert whose condition still holds opens again on the next one.
/// </summary>
/// <param name="dataSource">The database.</param>
internal sealed class AlertClearer(NpgsqlDataSource dataSource) : ITelemetryClearer
{
    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "TRUNCATE alerting.alert, alerting.alert_event, alerting.delivery");

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
