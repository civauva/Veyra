namespace Veyra.Core.Alerts;

/// <summary>
/// The thresholds of the built-in backup rules as they stand now: the full backup age rule and the log backup age
/// rule (ADR 0012, ADR 0020). The Backups tab judges each database's freshness by them, so the screen and the alerts
/// agree (ADR 0025).
/// </summary>
/// <param name="FullJudged">Whether the full backup age rule runs.</param>
/// <param name="FullCautionSeconds">The age of the last full backup that raises a caution, in seconds.</param>
/// <param name="FullWarningSeconds">The age of the last full backup that raises a warning, in seconds.</param>
/// <param name="IgnoredDatabases">
/// The databases not backed up on purpose; the full backup rule leaves them alone.
/// </param>
/// <param name="LogJudged">Whether the log backup age rule runs.</param>
/// <param name="LogCautionSeconds">
/// The age of the last log backup that raises a caution, in seconds; <see langword="null"/> when the rule has none.
/// </param>
/// <param name="LogWarningSeconds">The age of the last log backup that raises a warning, in seconds.</param>
public sealed record BackupThresholds(
    bool FullJudged,
    double FullCautionSeconds,
    double FullWarningSeconds,
    IReadOnlyList<string> IgnoredDatabases,
    bool LogJudged,
    double? LogCautionSeconds,
    double LogWarningSeconds)
{
    /// <summary>
    /// The rules' defaults: a full backup within 26 hours, a warning after 50; a log backup within an hour, a warning
    /// after four.
    /// </summary>
    public static BackupThresholds Default { get; } = new(true, 26 * 3600, 50 * 3600, [], true, 3600, 4 * 3600);

    /// <summary>Whether a database is one the full backup rule leaves alone, in any case.</summary>
    /// <param name="database">The database name.</param>
    /// <returns><see langword="true"/> when it is not backed up on purpose.</returns>
    public bool Ignores(string database) => IgnoredDatabases.Contains(database, StringComparer.OrdinalIgnoreCase);
}
