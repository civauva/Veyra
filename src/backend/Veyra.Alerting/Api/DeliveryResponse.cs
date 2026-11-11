using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Api;

/// <summary>One message about an alert event to one channel.</summary>
/// <param name="ChannelId">The channel's ID.</param>
/// <param name="ChannelName">The channel's name.</param>
/// <param name="ChannelKind">Webhook or email.</param>
/// <param name="State">Pending, sent, or failed for good.</param>
/// <param name="Attempts">How many tries were made.</param>
/// <param name="LastAttemptAt">When the last try ended.</param>
/// <param name="LastStatus">The HTTP status of the last webhook try, if there was a response.</param>
/// <param name="LastError">Why the last try failed.</param>
/// <param name="NextAttemptAt">When the next try is due, while pending.</param>
/// <param name="SentAt">When the channel accepted it.</param>
public sealed record DeliveryResponse(
    Guid ChannelId,
    string ChannelName,
    ChannelKind ChannelKind,
    DeliveryState State,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    int? LastStatus,
    string? LastError,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? SentAt);
