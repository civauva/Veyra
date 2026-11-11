using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Api;

/// <summary>
/// A notification channel as administrators see it. A webhook's URL is never included, only its start.
/// </summary>
/// <param name="Id">The channel's ID.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Webhook or email.</param>
/// <param name="Enabled">Whether it receives anything.</param>
/// <param name="SendWarning">Whether it hears of warnings.</param>
/// <param name="SendCaution">Whether it hears of cautions.</param>
/// <param name="SendResolved">Whether it hears that alerts it was told about resolved.</param>
/// <param name="WebhookUrlHint">The start of the webhook URL.</param>
/// <param name="EmailRecipients">The email addresses.</param>
/// <param name="CreatedAt">When it was added.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="Activity">What it did over the last 14 days.</param>
public sealed record ChannelResponse(
    Guid Id,
    string Name,
    ChannelKind Kind,
    bool Enabled,
    bool SendWarning,
    bool SendCaution,
    bool SendResolved,
    string? WebhookUrlHint,
    IReadOnlyList<string>? EmailRecipients,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ChannelActivityResponse Activity);
