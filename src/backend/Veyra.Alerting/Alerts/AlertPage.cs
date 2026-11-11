namespace Veyra.Alerting.Alerts;

/// <summary>One page of alerts, newest first.</summary>
/// <param name="Items">The alerts.</param>
/// <param name="Next">Where the next page starts; <see langword="null"/> on the last page.</param>
internal sealed record AlertPage(IReadOnlyList<AlertListEntry> Items, AlertCursor? Next);
