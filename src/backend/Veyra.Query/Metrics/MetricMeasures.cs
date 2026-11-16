using Veyra.Core.Metrics.Health;

namespace Veyra.Query.Metrics;

/// <summary>
/// The catalog measures read straight from a metric the agent sends (ADR 0020): the latest value per server and part.
/// A negative value means "none", such as a database in simple recovery for log backups, and is left out.
/// </summary>
internal static class MetricMeasures
{
    /// <summary>Each measure, the metric it comes from, and the attribute that names its part.</summary>
    public static readonly IReadOnlyList<(string Measure, string Metric, string Part)> All =
    [
        ("database-offline", "sqlserver.database.offline", SqlServerNames.DatabaseAttribute),
        ("log-used-ratio", "sqlserver.log.used.ratio", SqlServerNames.DatabaseAttribute),
        ("vlf-count", "sqlserver.log.vlf.count", SqlServerNames.DatabaseAttribute),
        ("volume-used-ratio", "sqlserver.volume.used.ratio", "volume"),
        ("log-backup-age", "sqlserver.database.log_backup.age", SqlServerNames.DatabaseAttribute),
        ("log-chain-broken", "sqlserver.database.log_chain.broken", SqlServerNames.DatabaseAttribute),
        ("checkdb-age", "sqlserver.database.checkdb.age", SqlServerNames.DatabaseAttribute),
        ("damaged-pages", "sqlserver.database.suspect_pages", SqlServerNames.DatabaseAttribute),
        ("cpu-ratio", "sqlserver.cpu.sql.ratio", ""),
        ("cpu-other-ratio", "sqlserver.cpu.other.ratio", ""),
        ("workers-ratio", "sqlserver.workers.ratio", ""),
        ("grants-pending", "sqlserver.memory.grants.pending", ""),
        ("os-memory-low", "sqlserver.os.memory.low", ""),
        ("memory-dumps", "sqlserver.memory.dumps.day", ""),
        ("agent-stopped", "sqlserver.agent.stopped", ""),
        ("restarted", "sqlserver.server.restarted", ""),
        ("unsupported", "sqlserver.server.unsupported", ""),
        ("job-late", "sqlserver.agent.job.late", SqlServerNames.JobAttribute),
        ("ag-unhealthy", "sqlserver.ag.replica.unhealthy", "replica"),
        ("ag-queue", "sqlserver.ag.queue", SqlServerNames.DatabaseAttribute),
        ("ag-not-synchronizing", "sqlserver.ag.not_synchronizing", SqlServerNames.DatabaseAttribute),
        ("logshipping-behind", "sqlserver.logshipping.behind", SqlServerNames.DatabaseAttribute),
    ];

    /// <summary>The metrics the measures come from.</summary>
    public static readonly IReadOnlyList<string> Names = All.Select(one => one.Metric)
        .Append("sqlserver.agent.job.running")
        .Append("sqlserver.agent.job.usual_duration")
        .Append("sqlserver.ag.primary")
        .Distinct()
        .ToList();

    /// <summary>Reads the measures of one server from its recent data. Pure; tested without a database.</summary>
    /// <param name="server">The server's recent data.</param>
    /// <param name="block">The server's block.</param>
    /// <param name="environment">The server's environment.</param>
    /// <returns>The measures.</returns>
    public static IEnumerable<DatabaseMeasure> Of(
        SeriesData server,
        Core.Telemetry.TelemetryBlock block,
        string? environment)
    {
        ArgumentNullException.ThrowIfNull(server);

        foreach (var (measure, metric, part) in All)
        {
            if (part.Length == 0)
            {
                if (server.Latest(server.Levels(server.Named(metric))) is { } whole and >= 0)
                {
                    yield return new DatabaseMeasure(block, environment, measure, null, whole);
                }

                continue;
            }

            foreach (var (name, value) in DatabaseView.LatestBy(server, metric, part))
            {
                if (value >= 0)
                {
                    yield return new DatabaseMeasure(block, environment, measure, name, value);
                }
            }
        }

        // A failover: whether a replica here was the primary of its group changed within the window read.
        foreach (var group in server
            .Named("sqlserver.ag.primary")
            .GroupBy(one => MetricNames.Attribute(one, "group_name") ?? ""))
        {
            var changed = group.Any(series =>
            {
                var levels = server.Levels([series]).Values.ToList();

                return levels.Count > 1 && levels.Min() != levels.Max();
            });

            yield return new DatabaseMeasure(block, environment, "ag-failover", group.Key, changed ? 1 : 0);
        }

        // A job running much longer than its usual duration, once it has run five minutes; 0 when it is not running.
        var usual = DatabaseView.LatestBy(server, "sqlserver.agent.job.usual_duration", SqlServerNames.JobAttribute);

        foreach (var (job, running) in DatabaseView.LatestBy(
            server,
            "sqlserver.agent.job.running",
            SqlServerNames.JobAttribute))
        {
            var typical = usual.GetValueOrDefault(job);

            yield return new DatabaseMeasure(
                block,
                environment,
                "job-overrun",
                job,
                running >= 300 && typical > 0 ? running / typical : 0);
        }
    }
}
