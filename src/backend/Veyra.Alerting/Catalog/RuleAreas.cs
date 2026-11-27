using Veyra.Alerting.Api;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Catalog;

/// <summary>
/// The area of a database server's screen each database rule belongs to (VEY-62): the tab where what it measures is
/// shown. The server tells it with each alert and ranks the areas by their alerts, so the browser maps nothing
/// (VEY-68). No rule watches the waits; they explain the others.
/// </summary>
internal static class RuleAreas
{
    /// <summary>The area of the server itself, which a rule without one of its own belongs to.</summary>
    public const string Server = "server";

    /// <summary>Every area, in the order of the server's tabs.</summary>
    public static readonly IReadOnlyList<string> All =
        ["live", "queries", "waits", "blocking", "databases", "jobs", "events", Server];

    private static readonly Dictionary<string, string> _areas = new(StringComparer.Ordinal)
    {
        ["db-tempdb-session"] = "live",

        ["db-plan-regression"] = "queries",
        ["db-long-query"] = "queries",

        ["db-blocking"] = "blocking",
        ["db-deadlocks"] = "blocking",
        ["db-head-blocker"] = "blocking",
        ["db-open-transaction"] = "blocking",

        ["db-backup-age"] = "databases",
        ["db-offline"] = "databases",
        ["db-log-full"] = "databases",
        ["db-vlf-count"] = "databases",
        ["db-volume-space"] = "databases",
        ["db-log-backup-age"] = "databases",
        ["db-log-chain"] = "databases",
        ["db-checkdb-age"] = "databases",
        ["db-damaged-pages"] = "databases",
        ["db-wraparound"] = "databases",

        ["db-agent-job"] = "jobs",
        ["db-agent-stopped"] = "jobs",
        ["db-job-overrun"] = "jobs",
        ["db-job-late"] = "jobs",

        ["db-severe-error"] = "events",
        ["db-memory-error"] = "events",
        ["db-login-failures"] = "events",
        ["db-memory-dump"] = "events",
    };

    /// <summary>The area of a rule's alerts. Pure.</summary>
    /// <param name="rule">The rule, such as <c>db-head-blocker</c>.</param>
    /// <returns>Its area; the server's for a rule without one of its own.</returns>
    public static string Of(string rule) => _areas.GetValueOrDefault(rule, Server);

    /// <summary>
    /// How each area is from a server's open alerts, the worst first; areas alike keep the order given, the order of
    /// the tabs. Pure.
    /// </summary>
    /// <param name="areas">The areas the screen shows, in the order of its tabs.</param>
    /// <param name="open">The server's open alerts.</param>
    /// <returns>Each area with its weather and its alerts, the warnings first.</returns>
    public static IReadOnlyList<AreaStateResponse> Rank(
        IReadOnlyList<string> areas,
        IReadOnlyList<AlertListItemResponse> open)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(open);

        // OrderBy is stable, so alike areas and alike alerts keep the order they came in.
        return
        [
            .. areas
                .Select(area =>
                {
                    var alerts = open
                        .Where(alert => Of(alert.Rule) == area)
                        .OrderByDescending(alert => alert.Severity)
                        .ToList();
                    var worst = alerts.Count == 0 ? (AlertSeverity?)null : alerts[0].Severity;

                    return new AreaStateResponse(area, worst?.ToString() ?? "Ok", alerts);
                })
                .OrderBy(state => state.Health switch { "Warning" => 0, "Caution" => 1, _ => 2 }),
        ];
    }
}
