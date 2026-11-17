using Veyra.Alerting.Catalog;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// The names of the built-in rules (ADR 0012), as they are stored with every alert and shown by the API.
/// </summary>
internal static class AlertRules
{
    /// <summary>The share of a service's log records that are errors.</summary>
    public const string ErrorRate = "error-rate";

    /// <summary>The share of a disk in use.</summary>
    public const string DiskSpace = "disk-space";

    /// <summary>The days until a disk is full at the pace of the last day.</summary>
    public const string DiskFilling = "disk-filling";

    /// <summary>The 95th percentile of a service's request durations.</summary>
    public const string LatencyP95 = "latency-p95";

    /// <summary>Restarts of a Docker container in the last 15 minutes (ADR 0018).</summary>
    public const string ContainerRestarts = "container-restarts";

    /// <summary>The share of its memory limit a Docker container uses.</summary>
    public const string ContainerMemory = "container-memory";

    /// <summary>Sessions of a database server blocked for five minutes (ADR 0018).</summary>
    public const string DatabaseBlocking = "db-blocking";

    /// <summary>Deadlocks on a database server in the last 15 minutes.</summary>
    public const string DatabaseDeadlocks = "db-deadlocks";

    /// <summary>The longest running query of a database.</summary>
    public const string DatabaseLongQuery = "db-long-query";

    /// <summary>The time since a database's last full backup.</summary>
    public const string DatabaseBackupAge = "db-backup-age";

    /// <summary>A SQL Agent job whose last run failed.</summary>
    public const string DatabaseAgentJob = "db-agent-job";

    /// <summary>The connections in use of a database server's maximum (PostgreSQL).</summary>
    public const string DatabaseConnections = "db-connections";

    /// <summary>The transaction ID age of a database, of the wraparound limit (PostgreSQL).</summary>
    public const string DatabaseWraparound = "db-wraparound";

    /// <summary>Every built-in rule, in the order screens list them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        ErrorRate,
        LatencyP95,
        DiskSpace,
        DiskFilling,
        ContainerRestarts,
        ContainerMemory,
        DatabaseBlocking,
        DatabaseDeadlocks,
        DatabaseLongQuery,
        DatabaseBackupAge,
        DatabaseAgentJob,
        DatabaseConnections,
        DatabaseWraparound,
        .. RuleCatalog.All.Select(rule => rule.Id),
    ];

    /// <summary>
    /// Whether a lower measurement is worse. True only for days until full: the fewer, the sooner the disk is full.
    /// The worst measurement of such an alert, its peak, is its lowest.
    /// </summary>
    /// <param name="rule">The rule's name.</param>
    /// <returns><see langword="true"/> when lower is worse.</returns>
    public static bool LowerIsWorse(string rule) =>
        rule == DiskFilling || RuleCatalog.Find(rule) is { LowerIsWorse: true };
}
