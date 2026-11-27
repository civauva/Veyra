namespace Veyra.Query.Backups.Api;

/// <summary>Which backups a database had on one day (ADR 0025).</summary>
/// <param name="Full">Whether a full backup finished.</param>
/// <param name="Differential">Whether a differential backup finished.</param>
/// <param name="Log">How many log backups finished.</param>
public sealed record BackupGridCellResponse(bool Full, bool Differential, int Log);
