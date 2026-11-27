namespace Veyra.Core.Activity;

/// <summary>
/// Counts a database server's backups (ADR 0025). The agent sends each backup as a <c>sqlserver.backup</c> record,
/// with the columns <c>database_name</c>, <c>backup_type</c>, and <c>started</c> (Unix milliseconds) and the time it
/// finished as the record's time; the Storage module counts them where they are kept, so a month of log backups is
/// never read row by row. The Query module reads through it.
/// </summary>
public interface IBackupHistory
{
    /// <summary>Counts a server's backups per day, database, and type over a period.</summary>
    /// <param name="block">The server's block, such as <c>microsoft.sql_server sql-prod-01</c>.</param>
    /// <param name="periodStart">The start of the period, inclusive.</param>
    /// <param name="periodEnd">The end of the period, inclusive.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One row per day, database, and type that had a backup, in no particular order.</returns>
    Task<IReadOnlyList<BackupDay>> ListDaysAsync(
        string block,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
