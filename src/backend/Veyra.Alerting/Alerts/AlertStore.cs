using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using Veyra.Alerting.Incidents;
using Veyra.Alerting.Notifications;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Reads and writes alerts in <c>alerting.alert</c>, with their history in <c>alerting.alert_event</c>. Other modules
/// read alerts through <see cref="IAlertStates"/>, one alert through <see cref="IAlertReader"/>, and their history
/// through <see cref="IAlertHistory"/>.
/// </summary>
/// <remarks>
/// Every change that is worth telling someone about queues its notifications in <c>alerting.delivery</c> in the same
/// transaction, so a change is never stored without its messages, or the other way round.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="grouping">
/// Decides which incident a new alert joins (ADR 0026); without it, alerts group by their own block and its host only.
/// </param>
internal sealed class AlertStore(NpgsqlDataSource dataSource, IncidentGrouping? grouping = null)
    : IAlertStates, IAlertReader, IAlertHistory
{
    // The incident an alert belongs to, and that incident's number, for an alert row read from alerting.alert.
    private const string _incidentColumns =
        """
        (SELECT member.incident_id FROM alerting.incident_alert AS member
         WHERE member.alert_id = alerting.alert.id),
        (SELECT incident.number FROM alerting.incident_alert AS member
         JOIN alerting.incident AS incident ON incident.id = member.incident_id
         WHERE member.alert_id = alerting.alert.id)
        """;

    private readonly IncidentGrouping _grouping = grouping ?? IncidentGrouping.Alone;

    // Each event that opens, raises, or eases an alert starts a stretch that ends at the alert's next event; the
    // resolving event starts none. The window function sees every event of the alert before the join keeps some.
    private const string _spells =
        """
        SELECT alert.id, alert.environment, alert.block_kind, alert.block_name, alert.part, spell.severity,
               spell.at, spell.until
        FROM alerting.alert AS alert
        JOIN LATERAL (
            SELECT event.kind, event.severity, event.at,
                   lead(event.at) OVER (ORDER BY event.at, event.id) AS until
            FROM alerting.alert_event AS event
            WHERE event.alert_id = alert.id
        ) AS spell ON spell.kind <> 4
        WHERE alert.opened_at < $2 AND (alert.resolved_at IS NULL OR alert.resolved_at >= $1)
        """;

    // ReadAllAsync reads them in this order.
    private const string _columns =
        "id, rule, environment, block_name, severity, value, peak, threshold, window_seconds, opened_at, "
        + "updated_at, resolved_at, block_kind, part";

    // Channels that want an alert of the given severity ($3): warnings, or cautions.
    private const string _queueBySeverity =
        """
        INSERT INTO alerting.delivery (event_id, channel_id, next_attempt_at)
        SELECT $1, id, $2
        FROM alerting.channel
        WHERE enabled AND CASE WHEN $3 = 2 THEN send_warning ELSE send_caution END
        """;

    // Channels that heard of the alert ($3) before, and want to know that it resolved.
    private const string _queueResolved =
        """
        INSERT INTO alerting.delivery (event_id, channel_id, next_attempt_at)
        SELECT $1, channel.id, $2
        FROM alerting.channel channel
        WHERE channel.enabled
          AND channel.send_resolved
          AND EXISTS (
              SELECT 1
              FROM alerting.delivery told
              JOIN alerting.alert_event event ON event.id = told.event_id
              WHERE event.alert_id = $3 AND told.channel_id = channel.id)
        """;

    /// <summary>
    /// Stores a newly opened alert, its opened event, the notifications about it, and its place in an incident
    /// (ADR 0026).
    /// </summary>
    /// <param name="alert">The alert, with no resolution time.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the alert is stored.</returns>
    /// <exception cref="PostgresException">The subject already has an open alert for the same rule.</exception>
    public async Task InsertAsync(AlertState alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // Read before the transaction, so a slow read of the series never holds the alert tables.
        var links = await _grouping.LinksAsync(cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = new NpgsqlCommand(
            $"""
            INSERT INTO alerting.alert ({_columns}, peak_at, worst_severity)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $10, $5)
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(alert.Id);
            command.Parameters.AddWithValue(alert.Rule);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)alert.Environment ?? DBNull.Value);
            command.Parameters.AddWithValue(alert.Subject.Name);
            command.Parameters.AddWithValue((short)alert.Severity);
            command.Parameters.AddWithValue(alert.Value);
            command.Parameters.AddWithValue(alert.Peak);
            command.Parameters.AddWithValue(alert.Threshold);
            command.Parameters.AddWithValue((int)alert.Window.TotalSeconds);
            command.Parameters.AddWithValue(alert.OpenedAt);
            command.Parameters.AddWithValue(alert.UpdatedAt);
            command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)alert.ResolvedAt ?? DBNull.Value);
            command.Parameters.AddWithValue(alert.Subject.KindName);
            command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)alert.Subject.Part ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var eventId = await AddEventAsync(
            transaction,
            alert.Id,
            AlertEventKind.Opened,
            alert.Severity,
            alert.Value,
            alert.OpenedAt,
            cancellationToken);
        await QueueAsync(
            transaction,
            _queueBySeverity,
            eventId,
            alert.OpenedAt,
            (short)alert.Severity,
            cancellationToken);
        await IncidentStore.JoinAsync(
            transaction,
            alert,
            NotificationMessages.Headline(alert),
            links,
            _grouping.Window,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Records a new measurement on an open alert. The peak only ever gets worse: higher, or lower for rules where
    /// less is worse (<see cref="AlertRules.LowerIsWorse"/>). A change of severity is recorded as an event; a rise to
    /// warning also queues notifications.
    /// </summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="severity">The severity it has now.</param>
    /// <param name="value">The latest measurement.</param>
    /// <param name="threshold">The threshold of that severity.</param>
    /// <param name="updatedAt">The time of the measurement.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the alert is updated.</returns>
    public async Task UpdateAsync(
        Guid id,
        AlertSeverity severity,
        double value,
        double threshold,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        AlertSeverity before;
        bool lowerIsWorse;

        await using (var command = new NpgsqlCommand(
            "SELECT severity, rule FROM alerting.alert WHERE id = $1 AND resolved_at IS NULL FOR UPDATE",
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(id);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            before = (AlertSeverity)reader.GetInt16(0);
            lowerIsWorse = AlertRules.LowerIsWorse(reader.GetString(1));
        }

        await using (var command = new NpgsqlCommand(
            """
            UPDATE alerting.alert
            SET severity = $2,
                worst_severity = greatest(worst_severity, $2),
                value = $3,
                peak_at = CASE WHEN (CASE WHEN $6 THEN $3 < peak ELSE $3 > peak END) THEN $5 ELSE peak_at END,
                peak = CASE WHEN $6 THEN least(peak, $3) ELSE greatest(peak, $3) END,
                threshold = $4,
                updated_at = $5
            WHERE id = $1
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue((short)severity);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(threshold);
            command.Parameters.AddWithValue(updatedAt);
            command.Parameters.AddWithValue(lowerIsWorse);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (severity != before)
        {
            var kind = severity > before ? AlertEventKind.Escalated : AlertEventKind.Eased;
            var eventId = await AddEventAsync(transaction, id, kind, severity, value, updatedAt, cancellationToken);

            // Easing is history only: a message that things got a little better is noise in the middle of an alert.
            if (kind == AlertEventKind.Escalated)
            {
                await QueueAsync(transaction, _queueBySeverity, eventId, updatedAt, (short)severity, cancellationToken);
                await IncidentStore.RaiseAsync(transaction, id, severity, updatedAt, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Closes an open alert, records the resolved event, and tells the channels that heard of the alert.
    /// </summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="value">The measurement that resolved it.</param>
    /// <param name="resolvedAt">The time of resolution.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the alert is resolved.</returns>
    public async Task ResolveAsync(Guid id, double value, DateTimeOffset resolvedAt, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        AlertSeverity severity;

        await using (var command = new NpgsqlCommand(
            """
            UPDATE alerting.alert
            SET value = $2, updated_at = $3, resolved_at = $3
            WHERE id = $1 AND resolved_at IS NULL
            RETURNING severity
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(value);
            command.Parameters.AddWithValue(resolvedAt);

            if (await command.ExecuteScalarAsync(cancellationToken) is not short stored)
            {
                return;
            }

            severity = (AlertSeverity)stored;
        }

        var eventId = await AddEventAsync(
            transaction,
            id,
            AlertEventKind.Resolved,
            severity,
            value,
            resolvedAt,
            cancellationToken);
        await QueueAsync(transaction, _queueResolved, eventId, resolvedAt, id, cancellationToken);
        await IncidentStore.ResolveIfLastAsync(transaction, id, resolvedAt, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Returns one alert, open or resolved.</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The alert, or <see langword="null"/> when there is none with this ID.</returns>
    public async Task<AlertState?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {_columns} FROM alerting.alert WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return (await ReadAllAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>Returns when an alert reached its peak.</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The time, or <see langword="null"/> for alerts opened before Veyra recorded it.</returns>
    public async Task<DateTimeOffset?> GetPeakAtAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT peak_at FROM alerting.alert WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return await command.ExecuteScalarAsync(cancellationToken) is DateTime peakAt
            ? new DateTimeOffset(peakAt, TimeSpan.Zero)
            : null;
    }

    /// <summary>Returns the history of one alert, oldest first.</summary>
    /// <param name="alertId">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The events, oldest first.</returns>
    public async Task<IReadOnlyList<AlertEvent>> ListEventsAsync(Guid alertId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT id, alert_id, kind, severity, value, at
            FROM alerting.alert_event
            WHERE alert_id = $1
            ORDER BY at, id
            """);
        command.Parameters.AddWithValue(alertId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<AlertEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AlertEvent(
                reader.GetInt64(0),
                reader.GetGuid(1),
                (AlertEventKind)reader.GetInt16(2),
                (AlertSeverity)reader.GetInt16(3),
                reader.GetDouble(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return events;
    }

    /// <summary>Lists the open alerts of one rule.</summary>
    /// <param name="rule">The rule's name.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The open alerts, in no particular order.</returns>
    public async Task<IReadOnlyList<AlertState>> ListOpenAsync(string rule, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM alerting.alert WHERE rule = $1 AND resolved_at IS NULL");
        command.Parameters.AddWithValue(rule);

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertState>> ListAsync(
        DateTimeOffset resolvedSince,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM alerting.alert WHERE resolved_at IS NULL OR resolved_at >= $1");
        command.Parameters.AddWithValue(resolvedSince);

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>
    /// Lists one page of alerts that were open at some time in a period, newest first, for the Alerts screen.
    /// </summary>
    /// <param name="query">The period, the filters, and where the page starts.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page, with a cursor to the next one when there is more.</returns>
    public async Task<AlertPage> ListPageAsync(AlertListQuery query, CancellationToken cancellationToken)
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
            SELECT {_columns}, worst_severity, peak_at, {_incidentColumns}
            FROM alerting.alert
            WHERE {Overlaps(query.From, query.To, Parameter)}
            """);

        if (query.Open is { } open)
        {
            sql.Append(open ? " AND resolved_at IS NULL" : " AND resolved_at IS NOT NULL");
        }

        if (query.Rules is { Count: > 0 } rules)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND rule = ANY({Parameter(rules.ToArray())})");
        }

        if (query.Severity is { } severity)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND worst_severity = {Parameter((short)severity)}");
        }

        if (query.Environment is { } environment)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND environment = {Parameter(environment)}");
        }

        if (query.Kind is { } kind)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND block_kind = {Parameter(AlertSubject.KindNameOf(kind))}");
        }

        if (!string.IsNullOrWhiteSpace(query.Block))
        {
            var pattern = Parameter("%" + EscapeLike(query.Block.Trim()) + "%");
            sql.Append(CultureInfo.InvariantCulture, $" AND (block_name ILIKE {pattern} OR part ILIKE {pattern})");
        }

        if (query.Name is { } name)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND block_name = {Parameter(name)}");
        }

        if (query.Part is { } part)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND part = {Parameter(part)}");
        }

        if (query.After is { } after)
        {
            if (query.WorstFirst)
            {
                var level = Parameter((short)(after.Severity ?? AlertSeverity.Warning));
                sql.Append(
                    CultureInfo.InvariantCulture,
                    $" AND (severity < {level} OR (severity = {level}"
                    + $" AND (opened_at, id) > ({Parameter(after.OpenedAt)}, {Parameter(after.Id)})))");
            }
            else
            {
                sql.Append(
                    CultureInfo.InvariantCulture,
                    $" AND (opened_at, id) < ({Parameter(after.OpenedAt)}, {Parameter(after.Id)})");
            }
        }

        // One more than the page, to know whether another page follows.
        var order = query.WorstFirst ? "severity DESC, opened_at, id" : "opened_at DESC, id DESC";
        sql.Append(
            CultureInfo.InvariantCulture,
            $" ORDER BY {order} LIMIT {Parameter(query.Limit + 1)}");
        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var entries = new List<AlertListEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(ReadEntry(reader));
        }

        if (entries.Count <= query.Limit)
        {
            return new AlertPage(entries, null);
        }

        var page = entries.Take(query.Limit).ToList();
        var last = page[^1].Alert;

        return new AlertPage(
            page,
            new AlertCursor(last.OpenedAt, last.Id, query.WorstFirst ? last.Severity : null));
    }

    /// <summary>
    /// Lists every alert that was open at some time in a period, reduced to what the Alerts screen counts.
    /// </summary>
    /// <param name="from">The start of the period.</param>
    /// <param name="to">The end of the period.</param>
    /// <param name="environment">Only alerts of this environment; <see langword="null"/> for all.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The alerts, in no particular order.</returns>
    public async Task<IReadOnlyList<AlertSpan>> ListSpansAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? environment,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand();

        string Parameter(object value)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });

            return $"${command.Parameters.Count}";
        }

        var sql = new StringBuilder(
            $"""
            SELECT rule, severity, worst_severity, opened_at, resolved_at
            FROM alerting.alert
            WHERE {Overlaps(from, to, Parameter)}
            """);

        if (environment is not null)
        {
            sql.Append(CultureInfo.InvariantCulture, $" AND environment = {Parameter(environment)}");
        }

        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var spans = new List<AlertSpan>();

        while (await reader.ReadAsync(cancellationToken))
        {
            spans.Add(new AlertSpan(
                reader.GetString(0),
                (AlertSeverity)reader.GetInt16(1),
                (AlertSeverity)reader.GetInt16(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return spans;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertSpell>> ListSpellsAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(_spells);
        command.Parameters.AddWithValue(periodStart);
        command.Parameters.AddWithValue(periodEnd);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var spells = new List<AlertSpell>();

        while (await reader.ReadAsync(cancellationToken))
        {
            spells.Add(new AlertSpell(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                ReadSubject(reader, kind: 2, name: 3, part: 4),
                (AlertSeverity)reader.GetInt16(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }

        return spells;
    }

    /// <summary>
    /// Reads the subject of an alert from a row. Kept here, next to the columns, for every query that reads alerts.
    /// </summary>
    /// <param name="reader">The row.</param>
    /// <param name="kind">The ordinal of <c>block_kind</c>.</param>
    /// <param name="name">The ordinal of <c>block_name</c>.</param>
    /// <param name="part">The ordinal of <c>part</c>.</param>
    /// <returns>The subject.</returns>
    public static AlertSubject ReadSubject(NpgsqlDataReader reader, int kind, int name, int part)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return new AlertSubject(
            AlertSubject.TryParseKind(reader.GetString(kind), out var parsed) ? parsed : AlertBlockKind.Service,
            reader.GetString(name),
            reader.IsDBNull(part) ? null : reader.GetString(part));
    }

    /// <summary>
    /// Deletes alerts that resolved before the given time, with their events and deliveries. Open alerts are never
    /// deleted.
    /// </summary>
    /// <param name="before">The cut-off time.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How many alerts were deleted.</returns>
    public async Task<int> DeleteResolvedBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "DELETE FROM alerting.alert WHERE resolved_at IS NOT NULL AND resolved_at < $1");
        command.Parameters.AddWithValue(before);

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);

        // Incidents share the alerts' retention (ADR 0026): one goes when the last of its alerts has gone.
        await IncidentStore.DeleteEmptyAsync(dataSource, cancellationToken);

        return deleted;
    }

    /// <summary>Lists the alerts of some incidents, as the alert list shows them, in the order they joined.</summary>
    /// <param name="incidentIds">The incidents.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Each alert with the incident it belongs to.</returns>
    public async Task<IReadOnlyList<IncidentAlertEntry>> ListForIncidentsAsync(
        IReadOnlyCollection<Guid> incidentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incidentIds);

        if (incidentIds.Count == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(
            $"""
            SELECT {_columns}, worst_severity, peak_at, {_incidentColumns}
            FROM alerting.alert
            JOIN alerting.incident_alert AS joined ON joined.alert_id = alerting.alert.id
            WHERE joined.incident_id = ANY($1)
            ORDER BY joined.joined_at, alerting.alert.id
            """);
        command.Parameters.AddWithValue(incidentIds.ToArray());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var entries = new List<IncidentAlertEntry>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var entry = ReadEntry(reader);
            entries.Add(new IncidentAlertEntry(entry.IncidentId!.Value, entry));
        }

        return entries;
    }

    /// <summary>Returns the incident an alert belongs to.</summary>
    /// <param name="alertId">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The incident's ID and number, or <see langword="null"/> when the alert belongs to none.</returns>
    public async Task<(Guid Id, long Number)?> GetIncidentOfAsync(Guid alertId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT incident.id, incident.number
            FROM alerting.incident_alert AS member
            JOIN alerting.incident AS incident ON incident.id = member.incident_id
            WHERE member.alert_id = $1
            """);
        command.Parameters.AddWithValue(alertId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetInt64(1)) : null;
    }

    /// <summary>Returns the history of every alert of an incident, oldest first.</summary>
    /// <param name="incidentId">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The events, oldest first.</returns>
    public async Task<IReadOnlyList<AlertEvent>> ListEventsOfIncidentAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT event.id, event.alert_id, event.kind, event.severity, event.value, event.at
            FROM alerting.alert_event AS event
            JOIN alerting.incident_alert AS member ON member.alert_id = event.alert_id
            WHERE member.incident_id = $1
            ORDER BY event.at, event.id
            """);
        command.Parameters.AddWithValue(incidentId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<AlertEvent>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new AlertEvent(
                reader.GetInt64(0),
                reader.GetGuid(1),
                (AlertEventKind)reader.GetInt16(2),
                (AlertSeverity)reader.GetInt16(3),
                reader.GetDouble(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return events;
    }

    private static async Task<long> AddEventAsync(
        NpgsqlTransaction transaction,
        Guid alertId,
        AlertEventKind kind,
        AlertSeverity severity,
        double value,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO alerting.alert_event (alert_id, kind, severity, value, at)
            VALUES ($1, $2, $3, $4, $5)
            RETURNING id
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue(alertId);
        command.Parameters.AddWithValue((short)kind);
        command.Parameters.AddWithValue((short)severity);
        command.Parameters.AddWithValue(value);
        command.Parameters.AddWithValue(at);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task QueueAsync(
        NpgsqlTransaction transaction,
        string sql,
        long eventId,
        DateTimeOffset due,
        object which,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, transaction.Connection, transaction);
        command.Parameters.AddWithValue(eventId);
        command.Parameters.AddWithValue(due);
        command.Parameters.AddWithValue(which);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AlertState>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var alerts = new List<AlertState>();

        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(Read(reader));
        }

        return alerts;
    }

    // One row of _columns.
    private static AlertState Read(NpgsqlDataReader reader) => new(
        Id: reader.GetGuid(0),
        Rule: reader.GetString(1),
        Environment: reader.IsDBNull(2) ? null : reader.GetString(2),
        Subject: ReadSubject(reader, kind: 12, name: 3, part: 13),
        Severity: (AlertSeverity)reader.GetInt16(4),
        Value: reader.GetDouble(5),
        Peak: reader.GetDouble(6),
        Threshold: reader.GetDouble(7),
        Window: TimeSpan.FromSeconds(reader.GetInt32(8)),
        OpenedAt: reader.GetFieldValue<DateTimeOffset>(9),
        UpdatedAt: reader.GetFieldValue<DateTimeOffset>(10),
        ResolvedAt: reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11));

    // One row of _columns, worst_severity, peak_at, and _incidentColumns.
    private static AlertListEntry ReadEntry(NpgsqlDataReader reader) => new(
        Read(reader),
        (AlertSeverity)reader.GetInt16(14),
        reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
        reader.IsDBNull(16) ? null : reader.GetGuid(16),
        reader.IsDBNull(17) ? null : reader.GetInt64(17));

    // Open at some time in the period: opened before it ended, and not resolved before it started.
    private static string Overlaps(DateTimeOffset from, DateTimeOffset to, Func<object, string> parameter) =>
        $"opened_at < {parameter(to)} AND (resolved_at IS NULL OR resolved_at >= {parameter(from)})";

    // The search box finds its text anywhere; % and _ in it are meant literally.
    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
