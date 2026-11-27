namespace Veyra.Query.Backups.Api;

/// <summary>The backups of one UTC day, per type (ADR 0025).</summary>
/// <param name="Day">The day, as <c>yyyy-MM-dd</c>.</param>
/// <param name="Full">The full backups.</param>
/// <param name="Differential">The differential backups.</param>
/// <param name="Log">The log backups.</param>
public sealed record BackupDayResponse(
    string Day,
    BackupTypeDayResponse Full,
    BackupTypeDayResponse Differential,
    BackupTypeDayResponse Log);
