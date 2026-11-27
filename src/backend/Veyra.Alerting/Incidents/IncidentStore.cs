using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Incidents;

/// <summary>
/// Reads and writes incidents in <c>alerting.incident</c>, which alerts they group in <c>alerting.incident_alert</c>,
/// and what people did to them in <c>alerting.incident_event</c> (ADR 0026). Other modules read incidents through
/// <see cref="IIncidentReader"/>.
/// </summary>
/// <remarks>
/// The alert store groups an alert into its incident, and resolves the incident, in the transaction that changes the
/// alert, through the static methods here. So an alert is never stored without its incident.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="alerts">The alerts, for the alerts of an incident.</param>
internal sealed class IncidentStore(NpgsqlDataSource dataSource, AlertStore alerts) : IIncidentReader
{
    // ReadIncident reads them in this order.
    private const string _columns =
        "id, number, environment, title, severity, state, opened_at, acknowledged_at, acknowledged_by_id, "
        + "acknowledged_by_name, resolved_at, updated_at";

    // Grouping reads the open incidents and then joins or opens one; two runs doing it at once could each open an
    // incident for related alerts. This lock, held to the end of the transaction, lets them take turns.
    private const long _groupingLock = 0x5665_7972_6100_0026;

    /// <summary>
    /// Puts a newly opened alert into an incident, in the alert's own transaction: an open incident of its environment
    /// with an alert on a related block that opened within the window, or a new one.
    /// </summary>
    /// <param name="transaction">The transaction that inserts the alert.</param>
    /// <param name="alert">The alert, already inserted.</param>
    /// <param name="title">The alert's headline, as notifications word it.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <param name="window">How long before the alert a related alert may have opened.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>The incident the alert is in now.</returns>
    public static async Task<Guid> JoinAsync(
        NpgsqlTransaction transaction,
        AlertState alert,
        string title,
        IReadOnlyList<BlockLink> links,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(alert);

        var connection = transaction.Connection!;

        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            command.Parameters.AddWithValue(_groupingLock);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var candidates = new List<IncidentCandidate>();

        // The unresolved incidents of an environment are few, and their alerts of the window fewer.
        await using (var command = new NpgsqlCommand(
            """
            SELECT incident.id, incident.severity, alert.block_kind, alert.block_name, alert.part, alert.opened_at
            FROM alerting.incident AS incident
            JOIN alerting.incident_alert AS member ON member.incident_id = incident.id
            JOIN alerting.alert AS alert ON alert.id = member.alert_id
            WHERE incident.state <> 'resolved'
              AND incident.environment IS NOT DISTINCT FROM $1
              AND alert.opened_at >= $2
              AND alert.id <> $3
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)alert.Environment ?? DBNull.Value);
            command.Parameters.AddWithValue(alert.OpenedAt - window);
            command.Parameters.AddWithValue(alert.Id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new IncidentCandidate(
                    reader.GetGuid(0),
                    (AlertSeverity)reader.GetInt16(1),
                    AlertStore.ReadSubject(reader, kind: 2, name: 3, part: 4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        var incidentId = IncidentGrouping.Choose(alert, candidates, links, window);

        if (incidentId is { } joined)
        {
            // A worse alert names the incident: its headline is what matters most now.
            await using var command = new NpgsqlCommand(
                """
                UPDATE alerting.incident
                SET title = CASE WHEN $2 > severity THEN $3 ELSE title END,
                    severity = greatest(severity, $2),
                    updated_at = $4
                WHERE id = $1
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(joined);
            command.Parameters.AddWithValue((short)alert.Severity);
            command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue(alert.OpenedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        else
        {
            joined = Guid.CreateVersion7(alert.OpenedAt);

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO alerting.incident (id, environment, title, severity, state, opened_at, updated_at)
                VALUES ($1, $2, $3, $4, 'open', $5, $5)
                """,
                connection,
                transaction);
            command.Parameters.AddWithValue(joined);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)alert.Environment ?? DBNull.Value);
            command.Parameters.AddWithValue(title);
            command.Parameters.AddWithValue((short)alert.Severity);
            command.Parameters.AddWithValue(alert.OpenedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
            "INSERT INTO alerting.incident_alert (alert_id, incident_id, joined_at) VALUES ($1, $2, $3)",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(alert.Id);
            command.Parameters.AddWithValue(joined);
            command.Parameters.AddWithValue(alert.OpenedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return joined;
    }

    /// <summary>Raises the severity of an alert's incident to the alert's, in the alert's own transaction.</summary>
    /// <param name="transaction">The transaction that changes the alert.</param>
    /// <param name="alertId">The alert.</param>
    /// <param name="severity">The severity the alert has now.</param>
    /// <param name="at">When it changed.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the incident is up to date.</returns>
    public static async Task RaiseAsync(
        NpgsqlTransaction transaction,
        Guid alertId,
        AlertSeverity severity,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using var command = new NpgsqlCommand(
            """
            UPDATE alerting.incident AS incident
            SET severity = $2, updated_at = $3
            FROM alerting.incident_alert AS member
            WHERE member.alert_id = $1 AND incident.id = member.incident_id AND incident.severity < $2
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue(alertId);
        command.Parameters.AddWithValue((short)severity);
        command.Parameters.AddWithValue(at);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Resolves an alert's incident when the alert was its last open one, in the transaction that resolves the alert.
    /// </summary>
    /// <param name="transaction">The transaction that resolves the alert.</param>
    /// <param name="alertId">The alert, already resolved.</param>
    /// <param name="at">When it resolved.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the incident is up to date.</returns>
    public static async Task ResolveIfLastAsync(
        NpgsqlTransaction transaction,
        Guid alertId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        await using (var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock($1)",
            transaction.Connection,
            transaction))
        {
            command.Parameters.AddWithValue(_groupingLock);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand(
            """
            UPDATE alerting.incident AS incident
            SET state = 'resolved', resolved_at = $2, updated_at = $2
            FROM alerting.incident_alert AS member
            WHERE member.alert_id = $1
              AND incident.id = member.incident_id
              AND incident.state <> 'resolved'
              AND NOT EXISTS (
                  SELECT 1
                  FROM alerting.incident_alert AS other
                  JOIN alerting.alert AS alert ON alert.id = other.alert_id
                  WHERE other.incident_id = incident.id AND alert.resolved_at IS NULL)
            """,
            transaction.Connection,
            transaction))
        {
            command.Parameters.AddWithValue(alertId);
            command.Parameters.AddWithValue(at);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deletes the resolved incidents left without alerts, after retention deleted their alerts: incidents are kept as
    /// long as their alerts (ADR 0026).
    /// </summary>
    /// <param name="dataSource">The database.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many incidents were deleted.</returns>
    public static async Task<int> DeleteEmptyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var command = dataSource.CreateCommand(
            """
            DELETE FROM alerting.incident AS incident
            WHERE incident.state = 'resolved'
              AND NOT EXISTS (SELECT 1 FROM alerting.incident_alert AS member WHERE member.incident_id = incident.id)
            """);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Returns one incident.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The incident, or <see langword="null"/> when there is none with this ID.</returns>
    public async Task<Incident?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {_columns} FROM alerting.incident WHERE id = $1");
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? ReadIncident(reader) : null;
    }

    /// <inheritdoc />
    async Task<IncidentState?> IIncidentReader.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken) is not { } incident)
        {
            return null;
        }

        var members = await alerts.ListForIncidentsAsync([id], cancellationToken);

        return new IncidentState(
            incident.Id,
            incident.Number,
            incident.Environment,
            incident.OpenedAt,
            incident.ResolvedAt,
            [.. members.Select(member => member.Entry.Alert)]);
    }

    /// <summary>
    /// Lists one page of incidents that were open at some time in a period: the open and acknowledged ones first, then
    /// the resolved ones, each the newest first.
    /// </summary>
    /// <param name="query">The period, the filters, and where the page starts.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page, with a cursor to the next one when there is more.</returns>
    public async Task<IncidentPage> ListPageAsync(IncidentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = dataSource.CreateCommand();

        string Parameter(object value)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });

            return $"${command.Parameters.Count}";
        }

        var sql = new StringBuilder(
            $"""
            SELECT {_columns}
            FROM alerting.incident
            WHERE opened_at < {Parameter(query.To)} AND (resolved_at IS NULL OR resolved_at >= {Parameter(query.From)})
            """);

        if (query.Status is { } status)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND state = {Parameter(IncidentStates.NameOf(status))}");
        }

        if (query.Environment is { } environment)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND environment = {Parameter(environment)}");
        }

        if (query.After is { } after)
        {
            var resolved = Parameter(after.Resolved);
            sql.Append(
                CultureInfo.InvariantCulture,
                $" AND ((state = 'resolved') > {resolved} OR ((state = 'resolved') = {resolved}"
                + $" AND (opened_at, id) < ({Parameter(after.OpenedAt)}, {Parameter(after.Id)})))");
        }

        // One more than the page, to know whether another page follows.
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY (state = 'resolved'), opened_at DESC, id DESC LIMIT {Parameter(query.Limit + 1)}");
        command.CommandText = sql.ToString();

        var incidents = new List<Incident>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                incidents.Add(ReadIncident(reader));
            }
        }

        if (incidents.Count <= query.Limit)
        {
            return new IncidentPage(incidents, null);
        }

        var page = incidents.Take(query.Limit).ToList();
        var last = page[^1];

        return new IncidentPage(
            page,
            new IncidentCursor(last.Status == IncidentStatus.Resolved, last.OpenedAt, last.Id));
    }

    /// <summary>
    /// Records that someone is on an incident (ADR 0026). It does not stop notifications. Acknowledging an
    /// acknowledged incident changes nothing, so two people pressing the button at once do not fight.
    /// </summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="userId">Who acknowledges it; <see langword="null"/> when the caller has no user ID.</param>
    /// <param name="userName">Their name, as it shows on the incident.</param>
    /// <param name="at">When.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The incident as it is now, or <see langword="null"/> when there is none with this ID.</returns>
    /// <exception cref="AlertingConflictException">The incident is resolved.</exception>
    public Task<Incident?> AcknowledgeAsync(
        Guid id,
        Guid? userId,
        string userName,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, IncidentEventKind.Acknowledged, userId, userName, at, cancellationToken);

    /// <summary>Takes an acknowledgement back. An incident nobody acknowledged stays as it is.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="userId">Who takes it back; <see langword="null"/> when the caller has no user ID.</param>
    /// <param name="userName">Their name, as it shows on the timeline.</param>
    /// <param name="at">When.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The incident as it is now, or <see langword="null"/> when there is none with this ID.</returns>
    /// <exception cref="AlertingConflictException">The incident is resolved.</exception>
    public Task<Incident?> UnacknowledgeAsync(
        Guid id,
        Guid? userId,
        string userName,
        DateTimeOffset at,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, IncidentEventKind.Unacknowledged, userId, userName, at, cancellationToken);

    /// <summary>Lists what people did to an incident, oldest first.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The acknowledgements and their taking back.</returns>
    public async Task<IReadOnlyList<IncidentEvent>> ListEventsAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT kind, actor_name, at
            FROM alerting.incident_event
            WHERE incident_id = $1
            ORDER BY at, id
            """);
        command.Parameters.AddWithValue(id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<IncidentEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new IncidentEvent(
                (IncidentEventKind)reader.GetInt16(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return events;
    }

    private async Task<Incident?> ChangeAsync(
        Guid id,
        IncidentEventKind kind,
        Guid? userId,
        string userName,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        Incident current;

        await using (var command = new NpgsqlCommand(
            $"SELECT {_columns} FROM alerting.incident WHERE id = $1 FOR UPDATE",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            current = ReadIncident(reader);
        }

        if (current.Status == IncidentStatus.Resolved)
        {
            throw new AlertingConflictException(
                $"{current.Key} is resolved; only an open incident can be acknowledged or have that taken back.");
        }

        var wanted = kind == IncidentEventKind.Acknowledged ? IncidentStatus.Acknowledged : IncidentStatus.Open;

        if (current.Status == wanted)
        {
            return current;
        }

        Incident changed;

        await using (var command = new NpgsqlCommand(
            $"""
            UPDATE alerting.incident
            SET state = $2, acknowledged_at = $3, acknowledged_by_id = $4, acknowledged_by_name = $5, updated_at = $6
            WHERE id = $1
            RETURNING {_columns}
            """,
            connection,
            transaction))
        {
            var acknowledged = kind == IncidentEventKind.Acknowledged;
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(IncidentStates.NameOf(wanted));
            command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, acknowledged ? at : (object)DBNull.Value);
            command.Parameters.AddWithValue(
                NpgsqlDbType.Uuid,
                acknowledged && userId is { } user ? user : (object)DBNull.Value);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, acknowledged ? userName : (object)DBNull.Value);
            command.Parameters.AddWithValue(at);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            changed = ReadIncident(reader);
        }

        await using (var command = new NpgsqlCommand(
            """
            INSERT INTO alerting.incident_event (incident_id, kind, actor_id, actor_name, at)
            VALUES ($1, $2, $3, $4, $5)
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue((short)kind);
            command.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)userId ?? DBNull.Value);
            command.Parameters.AddWithValue(userName);
            command.Parameters.AddWithValue(at);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return changed;
    }

    // One row of _columns.
    private static Incident ReadIncident(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        Number: reader.GetInt64(1),
        Environment: reader.IsDBNull(2) ? null : reader.GetString(2),
        Title: reader.GetString(3),
        Severity: (AlertSeverity)reader.GetInt16(4),
        Status: IncidentStates.TryParse(reader.GetString(5), out var status) ? status : IncidentStatus.Open,
        OpenedAt: reader.GetFieldValue<DateTimeOffset>(6),
        AcknowledgedAt: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
        AcknowledgedById: reader.IsDBNull(8) ? null : reader.GetGuid(8),
        AcknowledgedByName: reader.IsDBNull(9) ? null : reader.GetString(9),
        ResolvedAt: reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        UpdatedAt: reader.GetFieldValue<DateTimeOffset>(11));
}
