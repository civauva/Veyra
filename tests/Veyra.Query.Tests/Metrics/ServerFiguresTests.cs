using Veyra.Query.Metrics.Api;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Tests.Metrics;

/// <summary>Counts and lists the screens tell are worked out on the server (VEY-68).</summary>
public class ServerFiguresTests
{
    [Fact]
    public void Trouble_Availability_ListsEveryReplicaDatabaseAndLogShippingThatIsNotWell()
    {
        var availability = new AvailabilityResponse(
            [new("ag1", "sql-02", "SECONDARY", false), new("ag1", "sql-01", "PRIMARY", true)],
            [new("ag1", "sql-02", "shop", 0, 0, false), new("ag1", "sql-02", "audit", 0, 0, true)],
            [new("reporting", "secondary", true, 4000), new("archive", "secondary", false, 10)],
            []);

        Assert.Equal(
            [
                "replica sql-02 of ag1 is not healthy",
                "shop on sql-02 is not synchronizing",
                "log shipping of reporting is behind",
            ],
            availability.Trouble);
    }

    [Fact]
    public void FillingDisks_Host_CountsTheDisksThatFillAtTheirPace()
    {
        var host = new HostMetricsResponse(
            null!,
            "1h",
            60,
            [],
            [],
            [Disk("/", 12), Disk("/data", null), Disk("/logs", 300)],
            [],
            [],
            []);

        Assert.Equal(2, host.FillingDisks);
    }

    [Fact]
    public void QueryStoreOff_Queries_ListsTheDatabasesWithoutIt()
    {
        var queries = new Veyra.Query.Queries.Api.QueriesResponse(
            "1h",
            [],
            0,
            0,
            [new("shop", true, null), new("audit", false, "ALTER DATABASE [audit] SET QUERY_STORE = ON;")]);

        Assert.Equal("audit", Assert.Single(queries.QueryStoreOff).Database);
    }

    private static DiskResponse Disk(string mountpoint, double? days) =>
        new(mountpoint, "/dev/sda1", 50, 100, 0.5, days);
}
