namespace Veyra.Query.Backups.Api;

/// <summary>The last 7 UTC days of a window, and which backup types each database had on each (ADR 0025).</summary>
/// <param name="Days">The 7 days, as <c>yyyy-MM-dd</c>, the oldest first.</param>
/// <param name="Databases">Each database, by name, with one cell per day.</param>
public sealed record BackupGridResponse(IReadOnlyList<string> Days, IReadOnlyList<BackupGridRowResponse> Databases);
