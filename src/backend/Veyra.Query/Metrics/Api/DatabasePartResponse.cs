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
    double? LongestQuerySeconds);
