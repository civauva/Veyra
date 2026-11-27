namespace Veyra.Storage;

/// <summary>
/// Settings of the Storage module, bound from the <c>Veyra:Storage</c> section.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// PostgreSQL connection string. Supply it through configuration or the
    /// <c>Veyra__Storage__ConnectionString</c> environment variable; never commit it.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// How many whole days of logs to keep. Older daily partitions are dropped. Defaults to 14 days.
    /// </summary>
    public int LogRetentionDays { get; set; } = 14;

    /// <summary>
    /// The most disk space log storage may use, in gigabytes, indexes included. When it is exceeded, the oldest days
    /// are dropped first, even inside the retention period; today is always kept. Not set means no budget.
    /// </summary>
    public double? MaxLogStorageGigabytes { get; set; }

    /// <summary>
    /// How many whole days of metric points to keep. Older daily partitions are dropped, and series without points for
    /// longer are deleted. Defaults to 30 days (ADR 0010).
    /// </summary>
    public int MetricRetentionDays { get; set; } = 30;

    /// <summary>
    /// The most metric series stored at once. Points of new series beyond it are dropped and logged, so one attribute
    /// with a new value on every point cannot fill the disk. Defaults to 50,000 (ADR 0010).
    /// </summary>
    public int MaxMetricSeries { get; set; } = 50_000;

    /// <summary>
    /// How many days of traces to keep. Spans are the largest of the three signals, so the default is a week
    /// (ADR 0014).
    /// </summary>
    public int TraceRetentionDays { get; set; } = 7;

    /// <summary>
    /// The most disk trace storage may use, in gigabytes, or <see langword="null"/> for no budget. When it is exceeded,
    /// the oldest days are dropped first; today is always kept.
    /// </summary>
    public double? MaxTraceStorageGigabytes { get; set; }

    /// <summary>
    /// How many days of database servers' live sessions to keep: every read of the live view, every 10 seconds
    /// (ADR 0020). A week by default, enough to look back at last night's blocking.
    /// </summary>
    public int DatabaseSessionRetentionDays { get; set; } = 7;

    /// <summary>
    /// How many days of database servers' plans, deadlocks, events, and setting snapshots to keep (ADR 0020).
    /// </summary>
    public int DatabaseRecordRetentionDays { get; set; } = 90;

    /// <summary>
    /// How many days of job runs to keep (ADR 0027): 30 by default, the longest range a screen offers, so a month's
    /// success share outlives the 7 days of traces the runs were read from.
    /// </summary>
    public int JobRunRetentionDays { get; set; } = 30;

    /// <summary>How job runs are recognised in the traces (ADR 0027).</summary>
    public JobOptions Jobs { get; set; } = new();
}
