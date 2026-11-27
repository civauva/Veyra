using Veyra.Core.Alerts;

namespace Veyra.Query.Incidents;

/// <summary>
/// The stretch of time around an incident that its context reads (ADR 0026): from 30 minutes before it opened, to show
/// what normal looked like, to 10 minutes after it resolved, to show that it stayed resolved, or now while it is open.
/// Every read of the context stays inside it, which keeps it within the speed budgets (ADR 0015).
/// </summary>
/// <param name="From">The start.</param>
/// <param name="To">The end.</param>
internal sealed record IncidentWindow(DateTimeOffset From, DateTimeOffset To)
{
    private static readonly TimeSpan _before = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _after = TimeSpan.FromMinutes(10);

    // An alert's page shows the logs that led up to it, which start earlier than an incident's grouped first alert.
    private static readonly TimeSpan _beforeAlert = TimeSpan.FromHours(1);

    /// <summary>The window of an incident.</summary>
    /// <param name="incident">The incident.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static IncidentWindow Of(IncidentState incident, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var end = incident.ResolvedAt is { } resolvedAt && resolvedAt + _after < now ? resolvedAt + _after : now;

        return new IncidentWindow(incident.OpenedAt - _before, end);
    }

    /// <summary>
    /// The window of one alert (ADR 0029): from an hour before it opened until it resolved, or now while it is open.
    /// </summary>
    /// <param name="alert">The alert.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The window.</returns>
    public static IncidentWindow OfAlert(AlertState alert, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var end = alert.ResolvedAt is { } resolvedAt && resolvedAt < now ? resolvedAt : now;

        return new IncidentWindow(alert.OpenedAt - _beforeAlert, end);
    }
}
