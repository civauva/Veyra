using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Incidents;

namespace Veyra.Query.Tests.Incidents;

/// <summary>Which blocks run on which hosts, from the inventory of blocks that sent metrics (ADR 0026).</summary>
public sealed class BlockRelationsTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Links_ServicesDatabasesAndContainers_RunOnTheHostsTheirSeriesName()
    {
        var links = BlockRelations.Links(
        [
            Seen(AlertBlockKind.Service, "checkout", null, ["web-01", "web-02"]),
            Seen(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01", null, ["db-01"]),
            Seen(AlertBlockKind.Container, "checkout-api", "web-01", ["web-01"]),
            Seen(AlertBlockKind.Host, "web-01", null, ["web-01"]),
        ]);

        Assert.Equal(
            [
                new BlockLink(AlertBlockKind.Service, "checkout", "production", "web-01"),
                new BlockLink(AlertBlockKind.Service, "checkout", "production", "web-02"),
                new BlockLink(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01", "production", "db-01"),
                new BlockLink(AlertBlockKind.Container, "checkout-api", "production", "web-01"),
            ],
            links);
    }

    [Fact]
    public void Links_ContainerWithoutAHost_HasNone()
    {
        Assert.Empty(BlockRelations.Links([Seen(AlertBlockKind.Container, "checkout-api", null, [])]));
    }

    private static MetricBlockSeen Seen(AlertBlockKind kind, string name, string? host, string[] hosts) => new(
        new TelemetryBlock(kind, name),
        "production",
        host,
        hosts,
        1,
        new Dictionary<string, string>(),
        _noon.AddDays(-1),
        _noon);
}
