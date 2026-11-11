using Veyra.Alerting.Notifications;

namespace Veyra.Alerting.Api;

/// <summary>What a channel did lately.</summary>
/// <param name="Days">Per UTC day, oldest first: messages sent, and messages whose last try failed.</param>
/// <param name="Sent">Messages sent over all the days.</param>
/// <param name="Failed">Messages whose last try failed over all the days.</param>
/// <param name="LastAttemptAt">When the most recent try ended.</param>
/// <param name="LastError">Why the most recent try failed; <see langword="null"/> when it worked.</param>
/// <param name="LastStatus">The HTTP status of the most recent webhook try.</param>
/// <param name="NextAttemptAt">When the next retry is due, if one is waiting.</param>
public sealed record ChannelActivityResponse(
    IReadOnlyList<DayActivity> Days,
    int Sent,
    int Failed,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    int? LastStatus,
    DateTimeOffset? NextAttemptAt);
