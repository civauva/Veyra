using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>
/// Builds a SQL Server's availability (ADR 0020, area 12): Availability Groups, log shipping, and its cluster.
/// </summary>
/// <param name="search">The metric series and points.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class AvailabilityViewBuilder(IMetricSearch search, TimeProvider clock)
{
    private static readonly string[] _names =
    [
        "sqlserver.ag.replica.unhealthy",
        "sqlserver.ag.queue",
        "sqlserver.ag.lag",
        "sqlserver.ag.not_synchronizing",
        "sqlserver.logshipping.behind",
        "sqlserver.logshipping.delay",
        "sqlserver.cluster.node.owner",
    ];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the availability of a server.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>Its replicas, databases, log shipping, and nodes; empty lists when it has none.</returns>
    public async Task<AvailabilityResponse> AvailabilityAsync(
        string engine,
        string server,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                now.AddMinutes(-15),
                Names: _names,
                Limit: 5_000,
                Block: TelemetryBlock.Database(engine, server)),
            cancellationToken);

        if (series.Count == 0)
        {
            return new AvailabilityResponse([], [], [], []);
        }

        var recent = await _reader.ReadAsync(
            series,
            MetricWindow.Covering(now.AddMinutes(-15), now, 15),
            cancellationToken);
        double Latest(MetricSeries one) => recent.Latest(recent.Levels([one])) ?? 0;
        string Attribute(MetricSeries one, string key) => MetricNames.Attribute(one, key) ?? "";

        (string Group, string Replica, string Database) ReplicaDatabaseKey(MetricSeries one) => (
            Attribute(one, "group_name"),
            Attribute(one, "replica"),
            Attribute(one, SqlServerNames.DatabaseAttribute));

        var replicas = series.Where(one => one.Name == "sqlserver.ag.replica.unhealthy")
            .Select(one => new ReplicaResponse(
                Attribute(one, "group_name"),
                Attribute(one, "replica"),
                Attribute(one, "role"),
                Latest(one) < 1))
            .ToList();
        var databases = series.Where(one => one.Name == "sqlserver.ag.queue")
            .Select(one =>
            {
                var key = ReplicaDatabaseKey(one);
                var lag = series.FirstOrDefault(other => other.Name == "sqlserver.ag.lag"
                    && ReplicaDatabaseKey(other) == key);
                var sync = series.FirstOrDefault(other => other.Name == "sqlserver.ag.not_synchronizing"
                    && ReplicaDatabaseKey(other) == key);

                return new ReplicaDatabaseResponse(
                    key.Item1,
                    key.Item2,
                    key.Item3,
                    Latest(one),
                    lag is null ? 0 : Latest(lag),
                    sync is null || Latest(sync) < 1);
            })
            .ToList();
        var shipping = series.Where(one => one.Name == "sqlserver.logshipping.behind")
            .Select(one =>
            {
                var delay = series.FirstOrDefault(other => other.Name == "sqlserver.logshipping.delay"
                    && Attribute(other, SqlServerNames.DatabaseAttribute)
                        == Attribute(one, SqlServerNames.DatabaseAttribute)
                    && Attribute(other, "side") == Attribute(one, "side"));

                return new LogShippingResponse(
                    Attribute(one, SqlServerNames.DatabaseAttribute),
                    Attribute(one, "side"),
                    Latest(one) >= 1,
                    delay is null ? null : Latest(delay) is var seconds and >= 0 ? seconds : null);
            })
            .ToList();
        var nodes = series.Where(one => one.Name == "sqlserver.cluster.node.owner")
            .Select(one => new ClusterNodeResponse(Attribute(one, "node"), Attribute(one, "state"), Latest(one) >= 1))
            .ToList();

        return new AvailabilityResponse(replicas, databases, shipping, nodes);
    }
}
