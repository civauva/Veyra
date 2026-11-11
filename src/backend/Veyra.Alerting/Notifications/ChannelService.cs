using MimeKit;
using Npgsql;

namespace Veyra.Alerting.Notifications;

/// <summary>
/// Manages notification channels for administrators: checks what they enter, keeps webhook URLs encrypted, and sends
/// test messages.
/// </summary>
/// <param name="channels">Where channels are kept.</param>
/// <param name="deliveries">What channels did lately.</param>
/// <param name="urls">Encrypts webhook URLs.</param>
/// <param name="sender">Sends test messages.</param>
/// <param name="clock">The current time, for creation and change times.</param>
internal sealed class ChannelService(
    ChannelStore channels,
    DeliveryStore deliveries,
    WebhookUrlProtector urls,
    ChannelSender sender,
    TimeProvider clock)
{
    /// <summary>How many days of activity the Notifications screen shows per channel.</summary>
    public const int ActivityDays = 14;

    private const int _maxNameLength = 80;

    private const int _maxRecipients = 20;

    /// <summary>Lists every channel with what it did over the last <see cref="ActivityDays"/> days.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The channels by name, each with its activity.</returns>
    public async Task<IReadOnlyList<(NotificationChannel Channel, ChannelActivity Activity)>> ListAsync(
        CancellationToken cancellationToken)
    {
        var all = await channels.ListAsync(cancellationToken);
        var activity = await deliveries.GetActivityAsync(Today(), ActivityDays, cancellationToken);

        return all
            .Select(channel => (channel, activity.GetValueOrDefault(channel.Id) ?? NoActivity(channel.Id)))
            .ToList();
    }

    /// <summary>The activity of a channel that has not sent anything over the period.</summary>
    /// <param name="channelId">The channel's ID.</param>
    /// <returns>Empty days, oldest first, and no last try.</returns>
    public ChannelActivity NoActivity(Guid channelId)
    {
        var first = Today().AddDays(1 - ActivityDays);
        var days = Enumerable.Range(0, ActivityDays).Select(offset => new DayActivity(first.AddDays(offset), 0, 0));

        return new ChannelActivity(channelId, days.ToList(), null, null, null, null);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Adds a channel.</summary>
    /// <param name="input">The channel as entered.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The stored channel.</returns>
    /// <exception cref="AlertingValidationException">The input breaks a rule.</exception>
    /// <exception cref="AlertingConflictException">Another channel has the name.</exception>
    public async Task<NotificationChannel> CreateAsync(ChannelInput input, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var channel = Build(Guid.CreateVersion7(now), input, existing: null, now);

        return await SaveAsync(() => channels.InsertAsync(channel, cancellationToken));
    }

    /// <summary>Changes a channel. A webhook keeps its stored URL when the input leaves it out.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="input">The channel as it should be.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The stored channel, or <see langword="null"/> when there is none with this ID.</returns>
    /// <exception cref="AlertingValidationException">The input breaks a rule.</exception>
    /// <exception cref="AlertingConflictException">Another channel has the name.</exception>
    public async Task<NotificationChannel?> UpdateAsync(
        Guid id,
        ChannelInput input,
        CancellationToken cancellationToken)
    {
        if (await channels.GetAsync(id, cancellationToken) is not { } existing)
        {
            return null;
        }

        var channel = Build(id, input, existing, clock.GetUtcNow());

        return await SaveAsync(async () =>
            await channels.UpdateAsync(channel, cancellationToken) ? channel : (NotificationChannel?)null);
    }

    /// <summary>Deletes a channel and its delivery history.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns><see langword="true"/> if the channel existed.</returns>
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        channels.DeleteAsync(id, cancellationToken);

    /// <summary>Sends a test message to a stored channel, whether it is switched on or not.</summary>
    /// <param name="id">The channel's ID.</param>
    /// <param name="cancellationToken">Cancels the try.</param>
    /// <returns>How it went, or <see langword="null"/> when there is no channel with this ID.</returns>
    public async Task<SendResult?> TestAsync(Guid id, CancellationToken cancellationToken) =>
        await channels.GetAsync(id, cancellationToken) is { } channel
            ? await sender.SendAsync(channel, NotificationMessages.Test(channel.Name), cancellationToken)
            : null;

    /// <summary>Sends a test message to a channel that is not saved yet, or to changes not saved yet.</summary>
    /// <param name="input">The channel as entered.</param>
    /// <param name="id">
    /// The stored channel the input changes, so a webhook can be tested with its stored URL; <see langword="null"/>
    /// for a new channel.
    /// </param>
    /// <param name="cancellationToken">Cancels the try.</param>
    /// <returns>How it went.</returns>
    /// <exception cref="AlertingValidationException">The input breaks a rule.</exception>
    public async Task<SendResult> TestDraftAsync(ChannelInput input, Guid? id, CancellationToken cancellationToken)
    {
        var existing = id is { } known ? await channels.GetAsync(known, cancellationToken) : null;
        var draft = Build(existing?.Id ?? Guid.Empty, input, existing, clock.GetUtcNow());

        return await sender.SendAsync(draft, NotificationMessages.Test(draft.Name), cancellationToken);
    }

    private NotificationChannel Build(
        Guid id,
        ChannelInput input,
        NotificationChannel? existing,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);

        var name = input.Name?.Trim() ?? string.Empty;

        if (name.Length == 0 || name.Length > _maxNameLength)
        {
            throw new AlertingValidationException($"Give the channel a name of 1 to {_maxNameLength} characters.");
        }

        if (!input.SendWarning && !input.SendCaution)
        {
            throw new AlertingValidationException(
                "Choose warning, caution, or both: otherwise the channel hears nothing.");
        }

        var channel = new NotificationChannel(
            Id: id,
            Name: name,
            Kind: input.Kind,
            Enabled: input.Enabled,
            SendWarning: input.SendWarning,
            SendCaution: input.SendCaution,
            SendResolved: input.SendResolved,
            WebhookUrlProtected: null,
            WebhookUrlHint: null,
            EmailRecipients: null,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now);

        return input.Kind switch
        {
            ChannelKind.Webhook => WithWebhook(channel, input.WebhookUrl, existing),
            ChannelKind.Email => channel with { EmailRecipients = Recipients(input.EmailRecipients) },
            _ => throw new AlertingValidationException("A channel is a webhook or an email."),
        };
    }

    private NotificationChannel WithWebhook(NotificationChannel channel, string? url, NotificationChannel? existing)
    {
        // Changing a webhook without retyping its URL keeps the stored one; the screen never sees it again.
        if (string.IsNullOrWhiteSpace(url) && existing is { Kind: ChannelKind.Webhook, WebhookUrlProtected: { } kept })
        {
            return channel with { WebhookUrlProtected = kept, WebhookUrlHint = existing.WebhookUrlHint };
        }

        var parsed = WebhookUrls.Parse(url);

        return channel with { WebhookUrlProtected = urls.Protect(parsed), WebhookUrlHint = WebhookUrls.Hint(parsed) };
    }

    private static string[] Recipients(IReadOnlyList<string>? entered)
    {
        var addresses = (entered ?? [])
            .Select(entry => entry?.Trim() ?? string.Empty)
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (addresses.Length == 0 || addresses.Length > _maxRecipients)
        {
            throw new AlertingValidationException($"Enter 1 to {_maxRecipients} email addresses.");
        }

        foreach (var address in addresses)
        {
            // Only bare addresses: a display name or a second address in one entry would be stored as typed.
            var valid = MailboxAddress.TryParse(address, out var mailbox)
                && mailbox.Address == address
                && address.Contains('@', StringComparison.Ordinal);

            if (!valid)
            {
                throw new AlertingValidationException($"\"{address}\" is not an email address.");
            }
        }

        return addresses;
    }

    private static async Task<T> SaveAsync<T>(Func<Task<T>> save)
    {
        try
        {
            return await save();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new AlertingConflictException("Another channel already has this name.", exception);
        }
    }
}
