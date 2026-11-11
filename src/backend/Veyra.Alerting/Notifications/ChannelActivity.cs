namespace Veyra.Alerting.Notifications;

/// <summary>What a channel has done lately, for its card on the Notifications screen.</summary>
/// <param name="ChannelId">The channel's ID.</param>
/// <param name="Days">Sent and failed tries per day, oldest first, one entry per day of the period.</param>
/// <param name="LastAttemptAt">When the most recent try ended.</param>
/// <param name="LastError">Why the most recent try failed, or <see langword="null"/> when it worked.</param>
/// <param name="LastStatus">The HTTP status of the most recent webhook try, if there was a response.</param>
/// <param name="NextAttemptAt">When the next retry is due, if one is waiting.</param>
internal sealed record ChannelActivity(
    Guid ChannelId,
    IReadOnlyList<DayActivity> Days,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    int? LastStatus,
    DateTimeOffset? NextAttemptAt);
