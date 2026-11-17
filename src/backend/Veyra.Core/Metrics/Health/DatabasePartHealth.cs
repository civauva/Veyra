namespace Veyra.Core.Metrics.Health;

/// <summary>One database of a server, as the long-query, backup-age, and wraparound rules judge it.</summary>
/// <param name="Name">The database name.</param>
/// <param name="LongestQuerySeconds">
/// How long its longest running query has run now; <see langword="null"/> when the server does not say.
/// </param>
/// <param name="LongestQueryRecentMax">The longest any of its queries had run at any minute of the last five.</param>
/// <param name="BackupAgeSeconds">
/// Seconds since its last full backup; <see langword="null"/> when it has never had one.
/// </param>
public sealed record DatabasePartHealth(
    string Name,
    double? LongestQuerySeconds,
    double? LongestQueryRecentMax,
    double? BackupAgeSeconds)
{
    /// <summary>
    /// The age of its oldest unfrozen transaction ID of the wraparound limit, from 0 to 1; <see langword="null"/>
    /// when the server does not say, as only PostgreSQL does.
    /// </summary>
    public double? WraparoundRatio { get; init; }
}
