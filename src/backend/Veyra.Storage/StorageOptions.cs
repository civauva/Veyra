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
}
