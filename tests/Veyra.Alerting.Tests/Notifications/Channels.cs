using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Tests.Notifications;

/// <summary>Channels for tests: every severity and resolved, switched on.</summary>
internal static class Channels
{
    private static readonly DateTimeOffset _created = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A webhook channel. Its stored URL is a placeholder, not a real Data Protection payload.</summary>
    public static NotificationChannel Webhook(string name) => new(
        Id: Guid.CreateVersion7(),
        Name: name,
        Kind: ChannelKind.Webhook,
        Enabled: true,
        SendWarning: true,
        SendCaution: true,
        SendResolved: true,
        WebhookUrlProtected: "protected",
        WebhookUrlHint: "https://hooks.example.com/…",
        EmailRecipients: null,
        CreatedAt: _created,
        UpdatedAt: _created);

    /// <summary>An email channel to one address.</summary>
    public static NotificationChannel Email(string name, params string[] recipients) => new(
        Id: Guid.CreateVersion7(),
        Name: name,
        Kind: ChannelKind.Email,
        Enabled: true,
        SendWarning: true,
        SendCaution: true,
        SendResolved: true,
        WebhookUrlProtected: null,
        WebhookUrlHint: null,
        EmailRecipients: recipients.Length == 0 ? ["oncall@example.com"] : recipients,
        CreatedAt: _created,
        UpdatedAt: _created);
}
