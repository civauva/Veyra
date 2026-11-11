using Npgsql;
using NpgsqlTypes;

namespace Veyra.Alerting.Notifications;

/// <summary>Reads and writes notification channels in <c>alerting.channel</c>.</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class ChannelStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, name, kind, enabled, send_warning, send_caution, send_resolved, webhook_url_protected, "
        + "webhook_url_hint, email_recipients, created_at, updated_at";

    /// <summary>Stores a new channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The stored channel.</returns>
    /// <exception cref="PostgresException">Another channel has the same name, ignoring case.</exception>
    public async Task<NotificationChannel> InsertAsync(
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await using var command = dataSource.CreateCommand(
            $"INSERT INTO alerting.channel ({_columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)");
        AddParameters(command, channel);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return channel;
    }

    /// <summary>Replaces every field of a channel except its ID and creation time.</summary>
    /// <param name="channel">The channel as it should be stored.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><see langword="true"/> if the channel existed.</returns>
    /// <exception cref="PostgresException">Another channel has the same name, ignoring case.</exception>
    public async Task<bool> UpdateAsync(NotificationChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await using var command = dataSource.CreateCommand(
            """
            UPDATE alerting.channel
            SET name = $2, kind = $3, enabled = $4, send_warning = $5, send_caution = $6, send_resolved = $7,
                webhook_url_protected = $8, webhook_url_hint = $9, email_recipients = $10, updated_at = $12
            WHERE id = $1
            """);
        AddParameters(command, channel);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Deletes a channel and every delivery to it.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns><see langword="true"/> if the channel existed.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM alerting.channel WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Returns one channel.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The channel, or <see langword="null"/> when there is none with this ID.</returns>
    public async Task<NotificationChannel?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {_columns} FROM alerting.channel WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return (await ReadAllAsync(command, cancellationToken)).SingleOrDefault();
    }

    /// <summary>Lists every channel by name.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The channels, ordered by name ignoring case.</returns>
    public async Task<IReadOnlyList<NotificationChannel>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM alerting.channel ORDER BY lower(name)");

        return await ReadAllAsync(command, cancellationToken);
    }

    private static void AddParameters(NpgsqlCommand command, NotificationChannel channel)
    {
        command.Parameters.AddWithValue(channel.Id);
        command.Parameters.AddWithValue(channel.Name);
        command.Parameters.AddWithValue((short)channel.Kind);
        command.Parameters.AddWithValue(channel.Enabled);
        command.Parameters.AddWithValue(channel.SendWarning);
        command.Parameters.AddWithValue(channel.SendCaution);
        command.Parameters.AddWithValue(channel.SendResolved);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)channel.WebhookUrlProtected ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)channel.WebhookUrlHint ?? DBNull.Value);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            (object?)channel.EmailRecipients?.ToArray() ?? DBNull.Value);
        command.Parameters.AddWithValue(channel.CreatedAt);
        command.Parameters.AddWithValue(channel.UpdatedAt);
    }

    private static async Task<IReadOnlyList<NotificationChannel>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var channels = new List<NotificationChannel>();

        while (await reader.ReadAsync(cancellationToken))
        {
            channels.Add(new NotificationChannel(
                Id: reader.GetGuid(0),
                Name: reader.GetString(1),
                Kind: (ChannelKind)reader.GetInt16(2),
                Enabled: reader.GetBoolean(3),
                SendWarning: reader.GetBoolean(4),
                SendCaution: reader.GetBoolean(5),
                SendResolved: reader.GetBoolean(6),
                WebhookUrlProtected: reader.IsDBNull(7) ? null : reader.GetString(7),
                WebhookUrlHint: reader.IsDBNull(8) ? null : reader.GetString(8),
                EmailRecipients: reader.IsDBNull(9) ? null : reader.GetFieldValue<string[]>(9),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(10),
                UpdatedAt: reader.GetFieldValue<DateTimeOffset>(11)));
        }

        return channels;
    }
}
