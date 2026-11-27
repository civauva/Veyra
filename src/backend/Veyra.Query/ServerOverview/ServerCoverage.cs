using Veyra.Core.Metrics;
using Veyra.Query.Metrics;
using Veyra.Query.ServerOverview.Api;

namespace Veyra.Query.ServerOverview;

/// <summary>
/// Says which monitoring areas of ADR 0020 deliver data for a SQL Server, and for one that does not, which permission
/// the monitoring login lacks (ADR 0025). Pure; tested without a database.
/// </summary>
/// <remarks>
/// Each area is judged by what its reads always send: a server with nothing of an area to report, such as no enabled
/// SQL Agent jobs, sends nothing for it, and shows as not delivering. Availability is judged by the server read that
/// carries it, since a server without Availability Groups, log shipping, or a cluster sends no rows of its own for it.
/// </remarks>
internal static class ServerCoverage
{
    /// <summary>The metrics each area's reads send, besides the records and live reads.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Metrics = new Dictionary<string, string[]>
    {
        ["waits"] = [SqlServerNames.WaitTime],
        ["queries"] = [SqlServerNames.LongestQuery],
        ["storage"] = ["sqlserver.database.offline", "sqlserver.file.size", "sqlserver.volume.size"],
        ["backups"] =
        [
            SqlServerNames.BackupAge,
            "sqlserver.database.log_backup.age",
            "sqlserver.database.diff_backup.age",
        ],
        ["jobs"] =
        [
            SqlServerNames.JobFailed,
            SqlServerNames.JobLastRun,
            "sqlserver.agent.job.running",
            "sqlserver.agent.job.next_run",
        ],
        ["availability"] =
        [
            "sqlserver.server.info",
            "sqlserver.ag.replica.unhealthy",
            "sqlserver.logshipping.behind",
            "sqlserver.cluster.node.owner",
        ],
    };

    /// <summary>Every metric name coverage reads, for the series lookup.</summary>
    public static IEnumerable<string> Names => Metrics.Values.SelectMany(names => names);

    // The areas in the order ADR 0020 lists them, and the permissions each needs, the first missing one named.
    private static readonly (string Area, string[] Permissions)[] _areas =
    [
        ("activity", ["VIEW SERVER STATE", "VIEW DATABASE STATE"]),
        ("waits", ["VIEW SERVER STATE", "VIEW DATABASE STATE"]),
        ("queries", ["VIEW SERVER STATE", "VIEW DATABASE STATE", "VIEW ANY DEFINITION", "CONNECT ANY DATABASE"]),
        ("storage", ["VIEW SERVER STATE", "CONNECT ANY DATABASE"]),
        ("backups", ["SELECT ON msdb.dbo.backupset", "SELECT ON msdb.dbo.backupmediafamily"]),
        (
            "jobs",
            [
                "SELECT ON msdb.dbo.sysjobs",
                "SELECT ON msdb.dbo.sysjobhistory",
                "EXECUTE ON msdb.dbo.agent_datetime",
                "SELECT ON msdb.dbo.sysjobactivity",
                "SELECT ON msdb.dbo.sysjobsteps",
                "SELECT ON msdb.dbo.sysjobschedules",
                "SELECT ON msdb.dbo.sysschedules",
            ]),
        ("events", ["VIEW SERVER STATE"]),
        ("configuration", ["VIEW ANY DEFINITION", "VIEW SERVER STATE"]),
        (
            "availability",
            [
                "VIEW SERVER STATE",
                "SELECT ON msdb.dbo.log_shipping_monitor_primary",
                "SELECT ON msdb.dbo.log_shipping_monitor_secondary",
            ]),
    ];

    // Most reads run every minute or every 5 minutes. Events arrive only when they happen, and the configuration is
    // kept once a day per value, so a quiet hour or day is not a gap.
    private static readonly TimeSpan _usual = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan _events = TimeSpan.FromHours(1);

    private static readonly TimeSpan _configuration = TimeSpan.FromHours(26);

    /// <summary>Judges every area.</summary>
    /// <param name="reads">The overview's reads.</param>
    /// <param name="missing">The permissions the login lacks, as <c>/permissions</c> lists them.</param>
    /// <returns>One entry per area, in the order of ADR 0020.</returns>
    public static IReadOnlyList<CoverageResponse> Build(ServerOverviewReads reads, IReadOnlySet<string> missing)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(missing);

        var end = reads.Window.End;
        var lastSeen = reads.Series
            .GroupBy(series => series.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(series => series.LastSeen), StringComparer.Ordinal);

        DateTimeOffset? Seen(string area) =>
            area switch
            {
                "activity" => reads.Snapshot?.SampledAt,
                "events" => reads.EventSeen,
                "configuration" => reads.ConfigurationSeen,
                _ => Metrics[area]
                    .Select(name => lastSeen.TryGetValue(name, out var seen) ? seen : (DateTimeOffset?)null)
                    .Max(),
            };

        return _areas
            .Select(area =>
            {
                var seen = Seen(area.Area);
                var within = area.Area switch
                {
                    "events" => _events,
                    "configuration" => _configuration,
                    _ => _usual,
                };
                var delivering = seen is { } at && at >= end - within;

                return new CoverageResponse(
                    area.Area,
                    delivering,
                    seen,
                    delivering ? null : area.Permissions.FirstOrDefault(missing.Contains));
            })
            .ToList();
    }
}
