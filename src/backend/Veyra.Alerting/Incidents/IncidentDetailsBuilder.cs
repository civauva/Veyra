using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Api;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Incidents;

/// <summary>
/// Puts together what the Alerting module knows of an incident (ADR 0026): the incident, its alerts as the alert list
/// shows them, its figures, its timeline with the notifications sent, and the runbook of each of its rules. What
/// surrounds it, from traces, logs, and metrics, comes from the Query module.
/// </summary>
/// <param name="incidents">The incidents and their acknowledgements.</param>
/// <param name="alerts">The alerts and their events.</param>
/// <param name="deliveries">The notifications about them.</param>
/// <param name="options">The Alerting settings with the runbook links.</param>
/// <param name="clock">The current time, which ends an open incident's duration.</param>
internal sealed class IncidentDetailsBuilder(
    IncidentStore incidents,
    AlertStore alerts,
    DeliveryStore deliveries,
    IOptionsMonitor<AlertingOptions> options,
    TimeProvider clock)
{
    /// <summary>Builds the item of one incident, as the list shows it.</summary>
    /// <param name="incident">The incident.</param>
    /// <param name="cancellationToken">Cancels the read of its alerts.</param>
    /// <returns>The item.</returns>
    public async Task<IncidentItemResponse> ItemAsync(Incident incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var members = await alerts.ListForIncidentsAsync([incident.Id], cancellationToken);

        return IncidentItemResponse.Of(incident, [.. members.Select(member => member.Entry)]);
    }

    /// <summary>Builds the items of a page of incidents, reading the alerts of all of them at once.</summary>
    /// <param name="page">The incidents.</param>
    /// <param name="cancellationToken">Cancels the read of their alerts.</param>
    /// <returns>The items, in the page's order.</returns>
    public async Task<IReadOnlyList<IncidentItemResponse>> ItemsAsync(
        IReadOnlyList<Incident> page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var members = (await alerts.ListForIncidentsAsync([.. page.Select(incident => incident.Id)], cancellationToken))
            .ToLookup(member => member.IncidentId, member => member.Entry);

        return [.. page.Select(incident => IncidentItemResponse.Of(incident, [.. members[incident.Id]]))];
    }

    /// <summary>Builds the details of one incident.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The details, or <see langword="null"/> when there is no incident with this ID.</returns>
    public async Task<IncidentDetailsResponse?> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await incidents.FindAsync(id, cancellationToken) is not { } incident)
        {
            return null;
        }

        var members = (await alerts.ListForIncidentsAsync([id], cancellationToken))
            .Select(member => member.Entry)
            .ToList();

        // Retention deletes an incident right after its last alerts; in between it is as good as gone.
        if (members.Count == 0)
        {
            return null;
        }

        var events = await alerts.ListEventsOfIncidentAsync(id, cancellationToken);
        var sent = (await deliveries.ListForIncidentAsync(id, cancellationToken))
            .ToLookup(delivery => delivery.EventId);
        var acknowledgements = await incidents.ListEventsAsync(id, cancellationToken);
        var item = IncidentItemResponse.Of(incident, members);
        var byId = members.ToDictionary(member => member.Alert.Id);

        var timeline = events
            .Select(change =>
            {
                var alert = byId.GetValueOrDefault(change.AlertId)?.Alert;

                return new IncidentMomentResponse(
                    change.At,
                    change.Kind.ToString().ToLowerInvariant(),
                    change.AlertId,
                    alert?.Rule,
                    alert is null ? null : AlertBlockResponse.Of(alert),
                    change.Severity,
                    change.Value,
                    null,
                    [.. sent[change.Id].Select(AlertDetailsBuilder.Delivery)]);
            })
            .Concat(acknowledgements.Select(acknowledgement => new IncidentMomentResponse(
                acknowledgement.At,
                acknowledgement.Kind.ToString().ToLowerInvariant(),
                null,
                null,
                null,
                null,
                null,
                acknowledgement.ActorName,
                [])))
            .OrderBy(moment => moment.At)
            .ToList();

        var settings = options.CurrentValue;
        var runbooks = members
            .Select(member => member.Alert.Rule)
            .Distinct(StringComparer.Ordinal)
            .Select(rule => (Rule: rule, Url: settings.RunbookOf(rule)))
            .Where(runbook => runbook.Url is not null)
            .Select(runbook => new RunbookResponse(runbook.Rule, runbook.Url!))
            .ToList();

        return new IncidentDetailsResponse(
            item,
            [.. members.Select(AlertListItemResponse.Of)],
            Figures(incident, members, item.Blocks.Count, clock.GetUtcNow()),
            timeline,
            runbooks);
    }

    /// <summary>
    /// The figures of an incident. The leading alert is its worst, and of the worst the first to open: the one that
    /// best says what the incident is about. Pure.
    /// </summary>
    /// <param name="incident">The incident.</param>
    /// <param name="members">Its alerts, in the order they joined; at least one.</param>
    /// <param name="affectedBlocks">How many blocks its alerts are about.</param>
    /// <param name="now">The current time, which ends an open incident's duration.</param>
    /// <returns>The figures.</returns>
    public static IncidentFiguresResponse Figures(
        Incident incident,
        IReadOnlyList<AlertListEntry> members,
        int affectedBlocks,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(members);

        var leading = members
            .OrderByDescending(member => member.Worst)
            .ThenBy(member => member.Alert.OpenedAt)
            .First();

        return new IncidentFiguresResponse(
            incident.OpenedAt,
            ((incident.ResolvedAt ?? now) - incident.OpenedAt).TotalSeconds,
            affectedBlocks,
            new IncidentLeadingResponse(
                leading.Alert.Id,
                leading.Alert.Rule,
                leading.Alert.Value,
                leading.Alert.Threshold,
                leading.Alert.Peak,
                leading.PeakAt));
    }
}
