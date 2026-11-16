namespace Veyra.Storage.Activity;

/// <summary>
/// The daily partitioned tables of the store for database activity (ADR 0020), each with the setting that says how
/// many days it keeps.
/// </summary>
internal static class ActivityTables
{
    /// <summary>Every read of a server's busy sessions (area 3).</summary>
    public const string Sessions = "db_session";

    /// <summary>Plans, deadlocks, events, and snapshots, by kind and key.</summary>
    public const string Records = "db_record";

    /// <summary>The tables with their retention, in days, as the Storage settings say now.</summary>
    /// <param name="options">The Storage settings.</param>
    /// <returns>Each table and the days it keeps.</returns>
    public static IReadOnlyList<(string Table, int Days)> Retention(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return [(Sessions, options.DatabaseSessionRetentionDays), (Records, options.DatabaseRecordRetentionDays)];
    }

    /// <summary>Every table, for partitions and the storage footprint.</summary>
    public static readonly IReadOnlyList<string> All = [Sessions, Records];
}
