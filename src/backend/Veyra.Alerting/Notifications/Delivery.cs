namespace Veyra.Alerting.Notifications;

/// <summary>One message about one alert event to one channel, from <c>alerting.delivery</c>.</summary>
/// <param name="Id">The delivery's ID.</param>
/// <param name="EventId">The alert event it reports.</param>
/// <param name="ChannelId">The channel it goes to.</param>
/// <param name="ChannelName">The channel's name.</param>
/// <param name="ChannelKind">Webhook or email.</param>
/// <param name="State">Pending, sent, or failed for good.</param>
/// <param name="Attempts">How many tries were made.</param>
/// <param name="NextAttemptAt">When the next try is due, while pending.</param>
/// <param name="LastAttemptAt">When the last try ended.</param>
/// <param name="LastStatus">The HTTP status of the last webhook try, if there was a response.</param>
/// <param name="LastError">Why the last try failed.</param>
/// <param name="SentAt">When the channel accepted it.</param>
internal sealed record Delivery(
    long Id,
    long EventId,
    Guid ChannelId,
    string ChannelName,
    ChannelKind ChannelKind,
    DeliveryState State,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LastAttemptAt,
    int? LastStatus,
    string? LastError,
    DateTimeOffset? SentAt);
