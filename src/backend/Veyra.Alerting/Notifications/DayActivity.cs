namespace Veyra.Alerting.Notifications;

/// <summary>A channel's deliveries on one UTC day.</summary>
/// <param name="Day">The day.</param>
/// <param name="Sent">Messages the channel accepted.</param>
/// <param name="Failed">Tries that failed.</param>
public sealed record DayActivity(DateOnly Day, int Sent, int Failed);
