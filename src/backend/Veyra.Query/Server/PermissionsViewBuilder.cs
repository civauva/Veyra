using System.Globalization;
using System.Text;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>
/// Reads which permissions the monitoring login lacks, as the agent reports them every 5 minutes, and turns them into
/// the tabs they leave empty and the statements that grant them (VEY-58). Without it, a tab that cannot read its data
/// looks the same as one with nothing to show.
/// </summary>
/// <param name="search">The metric store.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class PermissionsViewBuilder(IMetricSearch search, TimeProvider clock)
{
    /// <summary>
    /// 1 for each permission the login lacks, 0 for each it has; labelled <c>permission</c> and <c>login</c>.
    /// </summary>
    public const string MissingName = "sqlserver.login.permission.missing";

    private const string _jobs = "SQL Agent jobs and their runs";

    private const string _schedules = "job schedules and next runs";

    private const string _msdb = " ON msdb.";

    // Granted inside an Azure SQL Database, to its contained user; there is no master to switch to (VEY-57).
    private static readonly HashSet<string> _database = new(StringComparer.Ordinal)
    {
        "VIEW DATABASE STATE",
        "VIEW DEFINITION",
    };

    // Every permission the agent checks, with the tabs it feeds and what it reads, as the SQL Server guide grants them.
    private static readonly Dictionary<string, (string[] Tabs, string Reads)> _known = new(StringComparer.Ordinal)
    {
        ["VIEW SERVER STATE"] = (
            ["live", "queries", "waits", "blocking", "databases", "events", "server"],
            "sessions, queries, waits, blocking, events, and the server's own figures"),
        ["VIEW ANY DEFINITION"] = (["queries", "server"], "query plans and the server's configuration"),
        ["VIEW DATABASE STATE"] = (
            ["live", "queries", "blocking", "databases"],
            "sessions, queries, blocking, and the database's figures"),
        ["VIEW DEFINITION"] = (["queries"], "query plans"),
        ["CONNECT ANY DATABASE"] = (["queries", "databases"], "Query Store, file space, and indexes in each database"),
        ["SELECT ON msdb.dbo.backupset"] = (["databases"], "backups"),
        ["SELECT ON msdb.dbo.backupmediafamily"] = (["databases"], "where backups are written"),
        ["SELECT ON msdb.dbo.suspect_pages"] = (["databases"], "damaged pages"),
        ["SELECT ON msdb.dbo.sysjobs"] = (["jobs"], _jobs),
        ["SELECT ON msdb.dbo.sysjobhistory"] = (["jobs"], _jobs),
        ["EXECUTE ON msdb.dbo.agent_datetime"] = (["jobs"], _jobs),
        ["SELECT ON msdb.dbo.sysjobsteps"] = (["jobs"], "the steps of each job run"),
        ["SELECT ON msdb.dbo.sysjobactivity"] = (["jobs"], "jobs running now"),
        ["SELECT ON msdb.dbo.sysjobschedules"] = (["jobs"], _schedules),
        ["SELECT ON msdb.dbo.sysschedules"] = (["jobs"], _schedules),
        ["SELECT ON msdb.dbo.log_shipping_monitor_primary"] = (["server"], "log shipping"),
        ["SELECT ON msdb.dbo.log_shipping_monitor_secondary"] = (["server"], "log shipping"),
    };

    private readonly MetricReader _reader = new(search);

    /// <summary>Reads what the login lacks on a server.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The gaps, and the script that closes them.</returns>
    public async Task<PermissionsResponse> PermissionsAsync(
        string engine,
        string server,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(
                now.AddHours(-1),
                Names: [MissingName],
                Limit: 200,
                Block: TelemetryBlock.Database(engine, server)),
            cancellationToken);
        var recent = await _reader.ReadAsync(
            series,
            MetricWindow.Covering(now.AddMinutes(-15), now, 15),
            cancellationToken);
        var missing = DatabaseView.LatestBy(recent, MissingName, "permission");
        var login = recent.Named(MissingName)
            .Select(one => MetricNames.Attribute(one, "login"))
            .FirstOrDefault(name => name is not null);
        var platform = recent.Named(MissingName)
            .Select(one => MetricNames.Attribute(one, "platform"))
            .FirstOrDefault(name => name is not null);

        return Build(missing, login, platform);
    }

    /// <summary>Turns the agent's report into gaps and a script. Pure; tested without a database.</summary>
    /// <param name="missing">The latest value of each permission: 1 when the login lacks it.</param>
    /// <param name="login">The login, or <see langword="null"/> when nothing was reported.</param>
    /// <param name="platform">
    /// The platform the agent reads, such as <c>managed-instance</c>, or <see langword="null"/> for an agent too old to
    /// say, which reads a SQL Server.
    /// </param>
    /// <returns>The gaps and the script.</returns>
    public static PermissionsResponse Build(
        IReadOnlyDictionary<string, double> missing,
        string? login,
        string? platform = null)
    {
        ArgumentNullException.ThrowIfNull(missing);

        if (missing.Count == 0)
        {
            return new PermissionsResponse(false, null, null, [], null);
        }

        var gaps = missing
            .Where(pair => pair.Value >= 1 && _known.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .Select(permission => new PermissionGapResponse(
                permission,
                _known[permission].Tabs,
                _known[permission].Reads))
            .ToList();

        return new PermissionsResponse(
            true,
            login,
            platform ?? "sqlserver",
            gaps,
            gaps.Count == 0 || login is null ? null : Script(gaps, login));
    }

    // The server permissions in master, then the msdb objects through a user the login may not have there yet.
    private static string Script(List<PermissionGapResponse> gaps, string login)
    {
        var name = $"[{login.Replace("]", "]]", StringComparison.Ordinal)}]";
        var database = gaps.Where(gap => _database.Contains(gap.Permission)).ToList();
        var server = gaps
            .Where(gap => !gap.Permission.Contains(_msdb, StringComparison.Ordinal)
                && !_database.Contains(gap.Permission))
            .ToList();
        var msdb = gaps.Where(gap => gap.Permission.Contains(_msdb, StringComparison.Ordinal)).ToList();
        var script = new StringBuilder();

        foreach (var gap in database)
        {
            script.Append(CultureInfo.InvariantCulture, $"GRANT {gap.Permission} TO {name};\n");
        }

        if (server.Count > 0)
        {
            script.Append("USE master;\n");

            foreach (var gap in server)
            {
                script.Append(CultureInfo.InvariantCulture, $"GRANT {gap.Permission} TO {name};\n");
            }

            script.Append("GO\n");
        }

        if (msdb.Count > 0)
        {
            var quoted = login.Replace("'", "''", StringComparison.Ordinal);

            script.Append("USE msdb;\n");
            script.Append(
                CultureInfo.InvariantCulture,
                $"IF USER_ID(N'{quoted}') IS NULL CREATE USER {name} FOR LOGIN {name};\n");

            foreach (var gap in msdb)
            {
                var permission = gap.Permission.Replace(_msdb, " ON ", StringComparison.Ordinal);

                script.Append(CultureInfo.InvariantCulture, $"GRANT {permission} TO {name};\n");
            }

            script.Append("GO\n");
        }

        return script.ToString().TrimEnd('\n');
    }
}
