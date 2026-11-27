namespace Veyra.Query.Backups.Api;

/// <summary>
/// How fresh one database's backups are, judged with the backup rules' thresholds, so the tab and the alerts agree
/// (ADR 0025).
/// </summary>
/// <param name="Database">The database.</param>
/// <param name="FullAgeSeconds">
/// Seconds since its last full backup; <see langword="null"/> when it never had one.
/// </param>
/// <param name="DiffAgeSeconds">
/// Seconds since its last differential backup; <see langword="null"/> when it never had one.
/// </param>
/// <param name="LogAgeSeconds">
/// Seconds since its last log backup, or since its creation without one; <see langword="null"/> in simple recovery,
/// which has no log backups.
/// </param>
/// <param name="RecoveryModel">Its recovery model as SQL Server names it, such as <c>FULL</c>.</param>
/// <param name="State">
/// <c>warning</c> or <c>caution</c> when a rule's threshold is passed, or no full backup was ever taken; <c>ok</c>;
/// or <c>ignored</c> when no backup rule judges it, such as a database not backed up on purpose.
/// </param>
/// <param name="Policy">What it is judged by, such as "Full backup within 26 h, warning after 50 h".</param>
public sealed record BackupFreshnessResponse(
    string Database,
    double? FullAgeSeconds,
    double? DiffAgeSeconds,
    double? LogAgeSeconds,
    string? RecoveryModel,
    string State,
    string Policy);
