namespace Veyra.Alerting.Notifications;

/// <summary>Sends a message to a channel of either kind.</summary>
/// <param name="webhooks">Posts to webhooks.</param>
/// <param name="email">Sends email.</param>
/// <param name="urls">Decrypts stored webhook URLs.</param>
internal sealed class ChannelSender(WebhookSender webhooks, EmailSender email, WebhookUrlProtector urls)
{
    /// <summary>Sends one message to one channel.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the try when Veyra stops.</param>
    /// <returns>How the try ended. Never throws for delivery failures.</returns>
    public Task<SendResult> SendAsync(
        NotificationChannel channel,
        NotificationMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (channel.Kind == ChannelKind.Email)
        {
            return email.SendAsync(channel.EmailRecipients ?? [], channel.Name, message, cancellationToken);
        }

        var url = channel.WebhookUrlProtected is { } stored ? urls.Unprotect(stored) : null;

        return url is null
            ? Task.FromResult(SendResult.Failed(
                "The stored webhook URL cannot be read: Veyra's encryption keys changed. Enter the URL again."))
            : webhooks.SendAsync(url, message, cancellationToken);
    }
}
