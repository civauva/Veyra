using Veyra.Core.Telemetry;
using Veyra.Query.Home;
using Veyra.Query.Metrics.Api;
using Veyra.Query.Overview;
using Veyra.Query.Resources;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Tests.Overview;

/// <summary>The databases of every server on one page, worst first (ADR 0023, point 6).</summary>
public class OverviewDatabasesTests
{
    private const double _day = 86_400;

    [Fact]
    public void List_LateBackupsAndDatabasesNotOnline_FirstThenTheLargest()
    {
        var prod = Server(
            "SQL-PROD-01",
            [Database("Shop", 50e9, _day / 4), Database("Reporting", 5e9, 2 * _day), Database("Archive", 1e9, 3600)],
            new Dictionary<string, string>
            {
                ["Shop"] = "ONLINE",
                ["Reporting"] = "ONLINE",
                ["Archive"] = "RECOVERY_PENDING",
            });
        var report = Server("SQL-RPT-01", [Database("Warehouse", 90e9, 3600)], new Dictionary<string, string>());

        var list = OverviewDatabases.List([prod, report]);

        Assert.Equal(
            [
                ("Reporting", "Online", true),
                ("Archive", "Recovery pending", false),
                ("Warehouse", null, false),
                ("Shop", "Online", false),
            ],
            list.Items.Select(item => (item.Name, item.State, item.Late)));
        Assert.Equal(4, list.Total);
        var reporting = list.Items[0];
        Assert.Equal(("SQL-PROD-01", "microsoft.sql_server"), (reporting.Server, reporting.Engine));
        Assert.Equal((5e9, 2 * _day), (reporting.SizeBytes, reporting.LastFullBackupAgeSeconds));
    }

    [Fact]
    public void List_ManyDatabases_IsCutAtTwelveAndCountsThemAll()
    {
        var server = Server(
            "pg-main",
            [.. Enumerable.Range(1, 20).Select(index => Database($"app{index}", index * 1e9, null) with
            {
                Connections = index,
            })],
            new Dictionary<string, string>(),
            "postgresql");

        var list = OverviewDatabases.List([server]);

        Assert.Equal(12, list.Items.Count);
        Assert.Equal(20, list.Total);
        Assert.Equal(("app20", 20d), (list.Items[0].Name, list.Items[0].Connections));
        Assert.All(list.Items, item => Assert.False(item.Late));
    }

    [Fact]
    public void List_RowsAsked_TakesThatManyAndCountsThemAll()
    {
        var server = Server(
            "pg-main",
            [.. Enumerable.Range(1, 40).Select(index => Database($"app{index}", index * 1e9, null))],
            new Dictionary<string, string>(),
            "postgresql");

        var list = OverviewDatabases.List([server], most: 30);

        Assert.Equal(30, list.Items.Count);
        Assert.Equal(40, list.Total);
        Assert.Equal("app40", list.Items[0].Name);
    }

    private static DatabasePartResponse Database(string name, double size, double? backupAge) =>
        new(name, size, null, null, backupAge, null, null);

    private static DatabaseServerListing Server(
        string name,
        IReadOnlyList<DatabasePartResponse> databases,
        IReadOnlyDictionary<string, string> states,
        string engine = "microsoft.sql_server") => new(
        TelemetryBlock.Database(engine, name),
        new DatabaseItemResponse(
            engine,
            name,
            null,
            BlockHealth.Ok,
            false,
            databases.Count,
            null,
            null,
            null,
            null,
            null,
            null),
        databases,
        [.. databases.Where(database => database.BackupAgeSeconds is >= 26 * 3600)],
        states);
}
