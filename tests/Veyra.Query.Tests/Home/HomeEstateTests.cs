using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Query.Home;

namespace Veyra.Query.Tests.Home;

/// <summary>
/// The home's estate, outlook, and counts are worked out on the server, so the dashboards only lay them out (VEY-68).
/// </summary>
public class HomeEstateTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_Services_AreByNameAndWorstFirstWithTheirCounts()
    {
        BlockResponse[] blocks =
        [
            Block("service", "billing", BlockHealth.Ok),
            Block("service", "checkout", BlockHealth.Caution),
            Block("service", "orders", BlockHealth.Warning),
            Block("service", "search", BlockHealth.Unknown),
            Block("database", "microsoft.sql_server sql-01", BlockHealth.Warning),
        ];

        var estate = HomeEstate.Build(blocks, [], []);

        Assert.Equal(["billing", "checkout", "orders", "search"], estate.Services.Select(block => block.Name));
        Assert.Equal(
            ["orders", "checkout", "billing", "search"],
            estate.ServicesWorstFirst.Select(block => block.Name));
        Assert.Equal(new HealthCountsResponse(1, 1, 1, 1), estate.ServiceCounts);
        Assert.Equal("microsoft.sql_server sql-01", Assert.Single(estate.Databases).Name);
        Assert.Equal(new HealthCountsResponse(1, 0, 0, 0), estate.DatabaseCounts);
    }

    [Fact]
    public void Build_Hosts_ShowTheDiskThatFillsSoonestOrElseTheFullest()
    {
        BlockResponse[] blocks =
        [
            Block("host", "web-02", BlockHealth.Caution),
            Block("host", "web-02", BlockHealth.Warning),
        ];
        HomeDiskResponse[] disks =
        [
            new("web-01", "/data", 0.58, null),
            new("web-01", "/", 0.30, null),
            new("db-01", "/var", 0.70, 40),
            new("db-01", "/", 0.50, 12),
        ];

        var estate = HomeEstate.Build(blocks, disks, []);

        Assert.Equal(["db-01", "web-01", "web-02"], estate.Hosts.Select(host => host.Name));
        Assert.Equal(new HostTileResponse("db-01", BlockHealth.Ok, disks[3], true), estate.Hosts[0]);
        Assert.Equal(new HostTileResponse("web-01", BlockHealth.Ok, disks[0], false), estate.Hosts[1]);
        Assert.Equal(new HostTileResponse("web-02", BlockHealth.Warning, null, false), estate.Hosts[2]);
        Assert.Equal(new HealthCountsResponse(1, 0, 2, 0), estate.HostCounts);
    }

    [Fact]
    public void Build_DiskOutlook_LeavesOutTheDisksWithAnAlertAndSaysWhenEveryOneHasOne()
    {
        HomeDiskResponse[] disks =
        [
            new("db-01", "/", 0.9, 3),
            new("db-01", "/var", 0.7, 20),
            new("web-01", "/", 0.2, null),
        ];
        HomeAlertResponse[] alerted = [Alert("host", "db-01", "/", AlertSeverity.Warning)];

        var some = HomeEstate.Build([], disks, alerted).DiskOutlook;
        var none = HomeEstate.Build([], disks[..1], alerted).DiskOutlook;

        Assert.Equal([disks[1]], some.Filling);
        Assert.Equal((2, false), (some.Hosts, some.AllAlerted));
        Assert.Empty(none.Filling);
        Assert.True(none.AllAlerted);
    }

    [Fact]
    public void Build_Alerts_AreCountedBySeverity()
    {
        HomeAlertResponse[] open =
        [
            Alert("service", "orders", null, AlertSeverity.Warning),
            Alert("service", "checkout", null, AlertSeverity.Caution),
            Alert("host", "web-01", "/", AlertSeverity.Caution),
        ];

        Assert.Equal(new AlertCountsResponse(1, 2), HomeEstate.Build([], [], open).AlertCounts);
    }

    [Fact]
    public void Totals_Hours_AddUpTheDayAndAverageThePastHoursWithoutTheOneStillFilling()
    {
        LogBucket[] observed =
        [
            new(_noon.AddHours(-2), 100, 4),
            new(_noon.AddHours(-1), 300, 6),
            new(_noon, 20, 1),
        ];

        var hourly = HomeEstate.Totals(new HourlyResponse(observed, [], 7));

        Assert.Equal((420L, 11L, 200d), (hourly.Records, hourly.Errors, hourly.AverageRecords));
    }

    private static BlockResponse Block(string kind, string name, BlockHealth health) =>
        new($"{kind}:production/{name}", kind, name, "production", health, [], 0, 0, null);

    private static HomeAlertResponse Alert(string kind, string name, string? part, AlertSeverity severity) =>
        new(
            1,
            Guid.CreateVersion7(),
            $"{kind}:{name}",
            null,
            kind,
            name,
            part,
            "production",
            "disk-filling",
            severity,
            1,
            1,
            1,
            60,
            _noon,
            null);
}
