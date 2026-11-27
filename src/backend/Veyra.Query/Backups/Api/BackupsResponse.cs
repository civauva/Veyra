namespace Veyra.Query.Backups.Api;

/// <summary>
/// A SQL Server's backup history over a window (ADR 0025): its backups, their time per day and type, a 7-day grid of
/// which types ran, and how fresh each database's backups are by the backup rules.
/// </summary>
/// <param name="From">The start of the window.</param>
/// <param name="To">The end of the window.</param>
/// <param name="SelfManaged">
/// Whether the platform backs up by itself and keeps no msdb history to read: Azure SQL Database and Managed Instance.
/// </param>
/// <param name="Backups">The backups that finished in the window, the newest first, at most 500.</param>
/// <param name="Days">Each UTC day the window touches, the oldest first, with the backups of the whole day.</param>
/// <param name="Grid">The last 7 UTC days of the window: each database and the types that ran each day.</param>
/// <param name="Freshness">Each database's backup ages judged by the backup rules, the worst first.</param>
public sealed record BackupsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    bool SelfManaged,
    IReadOnlyList<BackupResponse> Backups,
    IReadOnlyList<BackupDayResponse> Days,
    BackupGridResponse Grid,
    IReadOnlyList<BackupFreshnessResponse> Freshness);
