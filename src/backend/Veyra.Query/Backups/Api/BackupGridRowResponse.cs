namespace Veyra.Query.Backups.Api;

/// <summary>One database's row of the 7-day backup grid (ADR 0025).</summary>
/// <param name="Database">The database.</param>
/// <param name="Cells">One cell per day of the grid, the oldest first.</param>
public sealed record BackupGridRowResponse(string Database, IReadOnlyList<BackupGridCellResponse> Cells);
