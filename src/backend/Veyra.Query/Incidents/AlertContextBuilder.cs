using Microsoft.Extensions.Options;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Query.Incidents.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Gathers what surrounds one alert (ADR 0029): its block and the related blocks with their health, and their
/// warnings around its opening. The alert comes from the Alerting module through <see cref="IAlertReader"/>; the parts
/// are the incident context's, so an alert and an incident show their blocks and logs alike.
/// </summary>
/// <remarks>
/// The logs are read for the alert's window and its blocks only, and the relations come from the inventory kept for a
/// minute, so the read stays within the speed budgets of ADR 0015.
/// </remarks>
/// <param name="reader">The alerts.</param>
/// <param name="logs">The stored logs.</param>
/// <param name="alerts">The alerts of every block, for the health of the components.</param>
/// <param name="relations">Which blocks run on which hosts, and when blocks last sent.</param>
/// <param name="options">The Query settings with how long a block may stay quiet.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class AlertContextBuilder(
    IAlertReader reader,
    ILogSearch logs,
    IAlertStates alerts,
    BlockRelations relations,
    IOptionsMonitor<QueryOptions> options,
    TimeProvider clock)
{
    private readonly IncidentLogs _logs = new(logs);

    /// <summary>Gathers the context of one alert.</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The context, or <see langword="null"/> when there is no alert with this ID.</returns>
    public async Task<AlertContextResponse?> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await reader.GetAsync(id, cancellationToken) is not { } alert)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var window = IncidentWindow.OfAlert(alert, now);
        var links = await relations.ListLinksAsync(cancellationToken);

        // The logs need the related blocks; health needs the alerts and the inventory. Neither needs the other.
        var records = _logs.ReadAsync(
            IncidentComponents.AroundAlert(alert, links),
            alert.Environment,
            window,
            cancellationToken);
        var seen = relations.ListBlocksAsync(cancellationToken);
        var current = alerts.ListAsync(now, cancellationToken);

        await Task.WhenAll(records, seen, current);

        return new AlertContextResponse(
            window.From,
            window.To,
            await records,
            IncidentComponents.OfAlert(alert, links, await current, await seen, options.CurrentValue, now));
    }
}
