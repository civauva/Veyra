namespace Veyra.Alerting.Notifications;

/// <summary>A channel as an administrator enters it, to create one, change one, or test one before saving.</summary>
/// <param name="Name">The name people know it by, for example <c>#ops-alerts</c>. Unique, ignoring case.</param>
/// <param name="Kind">Webhook or email.</param>
/// <param name="Enabled">Whether it receives anything.</param>
/// <param name="SendWarning">Whether it hears of alerts that open as, or rise to, warning.</param>
/// <param name="SendCaution">Whether it hears of alerts that open as caution.</param>
/// <param name="SendResolved">Whether it hears that an alert it was told about has resolved.</param>
/// <param name="WebhookUrl">
/// For a webhook: the URL. When changing a webhook, leave it out to keep the stored one.
/// </param>
/// <param name="EmailRecipients">For email: the addresses, at most 20.</param>
public sealed record ChannelInput(
    string Name,
    ChannelKind Kind,
    bool Enabled,
    bool SendWarning,
    bool SendCaution,
    bool SendResolved,
    string? WebhookUrl,
    IReadOnlyList<string>? EmailRecipients);
