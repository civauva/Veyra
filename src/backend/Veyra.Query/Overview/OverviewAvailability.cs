using Veyra.Core.Alerts;
using Veyra.Query.Overview.Api;
using Veyra.Query.Resources;

namespace Veyra.Query.Overview;

/// <summary>
/// Works out the Overview's availability (ADR 0023, point 3): the share of time in which no service had a warning
/// open, from the alert history. Pure; tested without a database.
/// </summary>
/// <remarks>
/// It is not an uptime probe, since Veyra does not probe services. Cautions, and alerts about hosts, containers, and
/// database servers, do not count: only a warning on a service says its users are affected (ADR 0007).
/// </remarks>
internal static class OverviewAvailability
{
    /// <summary>How many days the figure covers, and how many before it the change compares with.</summary>
    public const int Days = 30;

    private static readonly TimeSpan _period = TimeSpan.FromDays(Days);

    /// <summary>The figure: the last <see cref="Days"/> days, compared with the days before them.</summary>
    /// <param name="spells">The alert history of both periods.</param>
    /// <param name="environment">Only alerts of this environment; all when null.</param>
    /// <param name="end">The end of the last period: the end of the window.</param>
    /// <param name="anyServices">Whether any service is known; without one there is nothing to be available.</param>
    /// <returns>The figure.</returns>
    public static AvailabilityFigureResponse Figure(
        IReadOnlyList<AlertSpell> spells,
        string? environment,
        DateTimeOffset end,
        bool anyServices)
    {
        ArgumentNullException.ThrowIfNull(spells);

        if (!anyServices)
        {
            return new AvailabilityFigureResponse(null, null, Days);
        }

        var current = Ratio(spells, environment, end - _period, end);
        var previous = Ratio(spells, environment, end - (2 * _period), end - _period);

        return new AvailabilityFigureResponse(current, ResourceComparison.Points(current, previous), Days);
    }

    /// <summary>The share of a period in which no service had a warning open.</summary>
    /// <param name="spells">The alert history.</param>
    /// <param name="environment">Only alerts of this environment; all when null.</param>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <returns>The share, from 0 to 1.</returns>
    public static double Ratio(
        IEnumerable<AlertSpell> spells,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd)
    {
        ArgumentNullException.ThrowIfNull(spells);

        var stretches = spells
            .Where(spell => spell.Subject.Kind == AlertBlockKind.Service && spell.Severity == AlertSeverity.Warning)
            .Where(spell => environment is null || spell.Environment == environment)
            .Select(spell => (
                From: Later(spell.From, periodStart),
                To: Earlier(spell.To ?? periodEnd, periodEnd)))
            .Where(stretch => stretch.From < stretch.To)
            .OrderBy(stretch => stretch.From)
            .ToList();

        // Warnings at the same time on several services are one stretch of time down, counted once.
        var down = TimeSpan.Zero;
        DateTimeOffset? openFrom = null;
        var openTo = DateTimeOffset.MinValue;

        foreach (var (from, to) in stretches)
        {
            if (openFrom is { } start && from > openTo)
            {
                down += openTo - start;
                openFrom = null;
            }

            openFrom ??= from;
            openTo = to > openTo ? to : openTo;
        }

        if (openFrom is { } last)
        {
            down += openTo - last;
        }

        return 1 - (down / (periodEnd - periodStart));
    }

    private static DateTimeOffset Later(DateTimeOffset one, DateTimeOffset other) => one > other ? one : other;

    private static DateTimeOffset Earlier(DateTimeOffset one, DateTimeOffset other) => one < other ? one : other;
}
