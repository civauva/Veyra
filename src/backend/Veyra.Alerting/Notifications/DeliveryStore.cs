using Npgsql;
using NpgsqlTypes;
using Veyra.Alerting.Alerts;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Reads and updates the queued notifications in <c>alerting.delivery</c>. The alert store queues them; the
/// dispatcher claims, sends, and settles them here.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class DeliveryStore(NpgsqlDataSource dataSource)
{
    /// <summary>
    /// Claims the deliveries that are due, oldest first. Each claimed delivery counts one more try and is leased: its
    /// next try moves to <paramref name="leaseUntil"/>, so a crash during sending only delays it.
    /// </summary>
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> lets two Veyra instances claim side by side without sending anything twice.
    /// </remarks>
    /// <param name="now">The current time.</param>
    /// <param name="limit">The most deliveries to claim.</param>
    /// <param name="leaseUntil">When a claimed delivery becomes due again if it is never settled.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>The claimed deliveries.</returns>
    public async Task<IReadOnlyList<DueDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        int limit,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            WITH claimed AS (
                UPDATE alerting.delivery
                SET attempts = attempts + 1, next_attempt_at = $3
                WHERE id IN (
                    SELECT id FROM alerting.delivery
                    WHERE state = 0 AND next_attempt_at <= $1
                    ORDER BY next_attempt_at, id
                    LIMIT $2
                    FOR UPDATE SKIP LOCKED)
                RETURNING id, attempts, event_id, channel_id)
            SELECT claimed.id, claimed.attempts,
                   e.id, e.alert_id, e.kind, e.severity, e.value, e.at,
                   a.rule, a.environment, a.block_name, a.severity, a.value, a.peak, a.threshold, a.window_seconds,
                   a.opened_at, a.updated_at, a.resolved_at,
                   c.id, c.name, c.kind, c.enabled, c.send_warning, c.send_caution, c.send_resolved,
                   c.webhook_url_protected, c.webhook_url_hint, c.email_recipients, c.created_at, c.updated_at,
                   a.block_kind, a.part
            FROM claimed
            JOIN alerting.alert_event e ON e.id = claimed.event_id
            JOIN alerting.alert a ON a.id = e.alert_id
            JOIN alerting.channel c ON c.id = claimed.channel_id
            ORDER BY claimed.id
            """);
        command.Parameters.AddWithValue(now);
        command.Parameters.AddWithValue(limit);
        command.Parameters.AddWithValue(leaseUntil);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var claimed = new List<DueDelivery>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var alertEvent = new AlertEvent(
                reader.GetInt64(2),
                reader.GetGuid(3),
                (AlertEventKind)reader.GetInt16(4),
                (AlertSeverity)reader.GetInt16(5),
                reader.GetDouble(6),
                reader.GetFieldValue<DateTimeOffset>(7));
            var alert = new AlertState(
                Id: alertEvent.AlertId,
                Rule: reader.GetString(8),
                Environment: reader.IsDBNull(9) ? null : reader.GetString(9),
                Subject: AlertStore.ReadSubject(reader, kind: 31, name: 10, part: 32),
                Severity: (AlertSeverity)reader.GetInt16(11),
                Value: reader.GetDouble(12),
                Peak: reader.GetDouble(13),
                Threshold: reader.GetDouble(14),
                Window: TimeSpan.FromSeconds(reader.GetInt32(15)),
                OpenedAt: reader.GetFieldValue<DateTimeOffset>(16),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(17),
                ResolvedAt: reader.IsDBNull(18) ? null : reader.GetFieldValue<DateTimeOffset>(18));
            var channel = new NotificationChannel(
                Id: reader.GetGuid(19),
                Name: reader.GetString(20),
                Kind: (ChannelKind)reader.GetInt16(21),
                Enabled: reader.GetBoolean(22),
                SendWarning: reader.GetBoolean(23),
                SendCaution: reader.GetBoolean(24),
                SendResolved: reader.GetBoolean(25),
                WebhookUrlProtected: reader.IsDBNull(26) ? null : reader.GetString(26),
                WebhookUrlHint: reader.IsDBNull(27) ? null : reader.GetString(27),
                EmailRecipients: reader.IsDBNull(28) ? null : reader.GetFieldValue<string[]>(28),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(29),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(30));

            claimed.Add(new DueDelivery(reader.GetInt64(0), reader.GetInt32(1), alertEvent, alert, channel));
        }

        return claimed;
    }

    /// <summary>Settles a delivery the channel accepted.</summary>
    /// <param name="id">The delivery's ID.</param>
    /// <param name="status">The HTTP status of a webhook's response; <see langword="null"/> for email.</param>
    /// <param name="at">When the channel accepted it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the delivery is settled.</returns>
    public async Task MarkSentAsync(long id, int? status, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE alerting.delivery
            SET state = 1, sent_at = $3, last_attempt_at = $3, last_status = $2, last_error = NULL
            WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue(at);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Records a failed try. With a next try time the delivery waits for it; without one it has failed for good.
    /// </summary>
    /// <param name="id">The delivery's ID.</param>
    /// <param name="error">Why it failed, in words for an administrator.</param>
    /// <param name="status">The HTTP status of a webhook's response, if there was one.</param>
    /// <param name="at">When the try ended.</param>
    /// <param name="nextAttemptAt">When to try again, or <see langword="null"/> to give up.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the try is recorded.</returns>
    public async Task MarkFailedAsync(
        long id,
        string error,
        int? status,
        DateTimeOffset at,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE alerting.delivery
            SET state = CASE WHEN $5::timestamptz IS NULL THEN 2 ELSE 0 END,
                next_attempt_at = coalesce($5, next_attempt_at),
                last_attempt_at = $4,
                last_status = $3,
                last_error = $2
            WHERE id = $1
            """);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(error);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)nextAttemptAt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Lists every delivery about one alert, in the order they were queued.</summary>
    /// <param name="alertId">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The deliveries with their channel's name and kind.</returns>
    public async Task<IReadOnlyList<Delivery>> ListForAlertAsync(Guid alertId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT d.id, d.event_id, d.channel_id, c.name, c.kind, d.state, d.attempts, d.next_attempt_at,
                   d.last_attempt_at, d.last_status, d.last_error, d.sent_at
            FROM alerting.delivery d
            JOIN alerting.alert_event e ON e.id = d.event_id
            JOIN alerting.channel c ON c.id = d.channel_id
            WHERE e.alert_id = $1
            ORDER BY d.id
            """);
        command.Parameters.AddWithValue(alertId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var deliveries = new List<Delivery>();

        while (await reader.ReadAsync(cancellationToken))
        {
            deliveries.Add(new Delivery(
                Id: reader.GetInt64(0),
                EventId: reader.GetInt64(1),
                ChannelId: reader.GetGuid(2),
                ChannelName: reader.GetString(3),
                ChannelKind: (ChannelKind)reader.GetInt16(4),
                State: (DeliveryState)reader.GetInt16(5),
                Attempts: reader.GetInt32(6),
                NextAttemptAt: reader.GetFieldValue<DateTimeOffset>(7),
                LastAttemptAt: reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                LastStatus: reader.IsDBNull(9) ? null : reader.GetInt32(9),
                LastError: reader.IsDBNull(10) ? null : reader.GetString(10),
                SentAt: reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return deliveries;
    }

    /// <summary>
    /// Summarises what every channel did over the last <paramref name="days"/> UTC days, today included.
    /// </summary>
    /// <param name="today">Today, in UTC.</param>
    /// <param name="days">How many days to cover.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One entry per channel that has any delivery.</returns>
    public async Task<IReadOnlyDictionary<Guid, ChannelActivity>> GetActivityAsync(
        DateOnly today,
        int days,
        CancellationToken cancellationToken)
    {
        var first = today.AddDays(1 - days);
        var counts = new Dictionary<Guid, Dictionary<DateOnly, (int Sent, int Failed)>>();
        var latest = new Dictionary<Guid, (DateTimeOffset? At, string? Error, int? Status, DateTimeOffset? Next)>();

        await using (var command = dataSource.CreateCommand(
            """
            SELECT channel_id, (last_attempt_at AT TIME ZONE 'UTC')::date AS day,
                   count(*) FILTER (WHERE state = 1), count(*) FILTER (WHERE last_error IS NOT NULL)
            FROM alerting.delivery
            WHERE last_attempt_at >= $1
            GROUP BY channel_id, day
            """))
        {
            command.Parameters.AddWithValue(new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var channel = reader.GetGuid(0);

                if (!counts.TryGetValue(channel, out var byDay))
                {
                    counts[channel] = byDay = [];
                }

                byDay[reader.GetFieldValue<DateOnly>(1)] = ((int)reader.GetInt64(2), (int)reader.GetInt64(3));
            }
        }

        // The most recent try of each channel, and its next retry if one is waiting.
        await using (var command = dataSource.CreateCommand(
            """
            SELECT DISTINCT ON (channel_id)
                   channel_id, last_attempt_at, last_error, last_status,
                   (SELECT min(next_attempt_at) FROM alerting.delivery waiting
                    WHERE waiting.channel_id = d.channel_id AND waiting.state = 0 AND waiting.attempts > 0)
            FROM alerting.delivery d
            WHERE last_attempt_at IS NOT NULL
            ORDER BY channel_id, last_attempt_at DESC, id DESC
            """))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                latest[reader.GetGuid(0)] = (
                    reader.GetFieldValue<DateTimeOffset>(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4));
            }
        }

        var activity = new Dictionary<Guid, ChannelActivity>();

        foreach (var channel in counts.Keys.Union(latest.Keys))
        {
            var byDay = counts.GetValueOrDefault(channel) ?? [];
            var series = Enumerable.Range(0, days)
                .Select(offset => first.AddDays(offset))
                .Select(day =>
                {
                    var (sent, failed) = byDay.GetValueOrDefault(day);

                    return new DayActivity(day, sent, failed);
                })
                .ToList();
            var (at, error, status, next) = latest.GetValueOrDefault(channel);

            activity[channel] = new ChannelActivity(channel, series, at, error, status, next);
        }

        return activity;
    }
}
