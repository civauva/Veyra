using Veyra.Alerting.Alerts;
using Veyra.Alerting.Api;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.Catalog;

/// <summary>
/// Every database rule belongs to an area of the server's screen, and the server ranks the areas by their alerts, so
/// the browser neither maps nor sorts them (VEY-68).
/// </summary>
public class RuleAreasTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Of_EveryDatabaseRule_HasAKnownArea()
    {
        var rules = RuleCatalog.All.Select(rule => rule.Id).Where(id => id.StartsWith("db-", StringComparison.Ordinal));

        Assert.All(rules, rule => Assert.Contains(RuleAreas.Of(rule), RuleAreas.All));
        Assert.Equal("blocking", RuleAreas.Of("db-head-blocker"));
        Assert.Equal("server", RuleAreas.Of("disk-space"));
    }

    [Fact]
    public void Rank_OpenAlerts_ListsTheWorstAreaFirstAndKeepsTheTabOrderOtherwise()
    {
        AlertListItemResponse[] open =
        [
            Item("db-backup-age", AlertSeverity.Warning),
            Item("db-head-blocker", AlertSeverity.Caution),
            Item("db-long-query", AlertSeverity.Caution),
            Item("db-log-full", AlertSeverity.Caution),
        ];

        var states = RuleAreas.Rank(["live", "queries", "blocking", "databases", "jobs"], open);

        Assert.Equal(
            [("databases", "Warning", 2), ("queries", "Caution", 1), ("blocking", "Caution", 1), ("live", "Ok", 0),
                ("jobs", "Ok", 0)],
            states.Select(state => (state.Area, state.Health, state.Alerts.Count)));
        Assert.Equal(["db-backup-age", "db-log-full"], states[0].Alerts.Select(alert => alert.Rule));
    }

    private static AlertListItemResponse Item(string rule, AlertSeverity severity) =>
        AlertListItemResponse.Of(new AlertListEntry(
            new AlertState(
                Guid.CreateVersion7(),
                rule,
                "production",
                AlertSubject.Database("microsoft.sql_server sql-prod-01", null),
                severity,
                1,
                1,
                1,
                TimeSpan.FromMinutes(5),
                _noon,
                _noon,
                null),
            severity,
            null));
}
