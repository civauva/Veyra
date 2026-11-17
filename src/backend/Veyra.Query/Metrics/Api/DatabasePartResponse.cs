namespace Veyra.Query.Metrics.Api;

/// <summary>One database of a server.</summary>
/// <param name="Name">The database name.</param>
/// <param name="SizeBytes">Its data and log files together.</param>
/// <param name="LogBytes">Its log files.</param>
/// <param name="GrowthBytesPerDay">How much it grew over the last day, or null without a day of points.</param>
/// <param name="BackupAgeSeconds">Time since its last full backup, or null when it has never had one.</param>
/// <param name="IoLatencySeconds">The average time of a file read or write over the window.</param>
/// <param name="LongestQuerySeconds">The longest query running in it now.</param>
public sealed record DatabasePartResponse(
    string Name,
    double? SizeBytes,
    double? LogBytes,
    double? GrowthBytesPerDay,
    double? BackupAgeSeconds,
    double? IoLatencySeconds,
    double? LongestQuerySeconds)
{
    /// <summary>Connections to it now (PostgreSQL).</summary>
    public double? Connections { get; init; }

    /// <summary>The age of its oldest unfrozen transaction ID, in transactions (PostgreSQL).</summary>
    public double? TransactionIdAge { get; init; }

    /// <summary>That age of the wraparound limit of 2^31, from 0 to 1 (PostgreSQL).</summary>
    public double? WraparoundRatio { get; init; }

    /// <summary>Live rows of all its tables (PostgreSQL).</summary>
    public double? LiveRows { get; init; }

    /// <summary>Dead rows of all its tables, left behind until a vacuum clears them (PostgreSQL).</summary>
    public double? DeadRows { get; init; }

    /// <summary>Dead rows of all its rows, from 0 to 1 (PostgreSQL).</summary>
    public double? DeadRowsRatio { get; init; }

    /// <summary>How many times its tables were vacuumed over the last day (PostgreSQL).</summary>
    public double? Vacuums { get; init; }
}
