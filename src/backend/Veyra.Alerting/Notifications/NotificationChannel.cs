namespace Veyra.Alerting.Notifications;

/// <summary>A place where Veyra sends alert notifications, from <c>alerting.channel</c>.</summary>
/// <param name="Id">The channel's ID.</param>
/// <param name="Name">The name people know it by, unique ignoring case.</param>
/// <param name="Kind">Webhook or email.</param>
/// <param name="Enabled">Whether it receives anything. A channel that is off gets nothing, not even later.</param>
/// <param name="SendWarning">Whether it hears of alerts that open as, or rise to, warning.</param>
/// <param name="SendCaution">Whether it hears of alerts that open as caution.</param>
/// <param name="SendResolved">Whether it hears that an alert it was told about has resolved.</param>
/// <param name="WebhookUrlProtected">The webhook URL, encrypted with Data Protection. Null for email.</param>
/// <param name="WebhookUrlHint">The start of the webhook URL, safe to show. Null for email.</param>
/// <param name="EmailRecipients">The email addresses. Null for webhooks.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
internal sealed record NotificationChannel(
    Guid Id,
    string Name,
    ChannelKind Kind,
    bool Enabled,
    bool SendWarning,
    bool SendCaution,
    bool SendResolved,
    string? WebhookUrlProtected,
    string? WebhookUrlHint,
    IReadOnlyList<string>? EmailRecipients,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
