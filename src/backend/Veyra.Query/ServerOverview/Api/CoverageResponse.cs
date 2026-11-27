namespace Veyra.Query.ServerOverview.Api;

/// <summary>Whether one monitoring area of ADR 0020 delivers data (ADR 0025).</summary>
/// <param name="Area">
/// <c>activity</c>, <c>waits</c>, <c>queries</c>, <c>storage</c>, <c>backups</c>, <c>jobs</c>, <c>events</c>,
/// <c>configuration</c>, or <c>availability</c>.
/// </param>
/// <param name="Delivering">
/// Whether data arrived recently: in the last 15 minutes, an hour for events, and a day for the configuration, which
/// Veyra keeps once a day.
/// </param>
/// <param name="LastSeen">When the area's data last arrived, as far as Veyra keeps it; null when never.</param>
/// <param name="MissingPermission">
/// When the area does not deliver, the permission the monitoring login lacks for it, from <c>/permissions</c>.
/// </param>
public sealed record CoverageResponse(
    string Area,
    bool Delivering,
    DateTimeOffset? LastSeen,
    string? MissingPermission);
