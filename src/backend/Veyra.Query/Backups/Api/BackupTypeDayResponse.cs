namespace Veyra.Query.Backups.Api;

/// <summary>How many backups of one type finished on a day, and how long they took together (ADR 0025).</summary>
/// <param name="Count">How many.</param>
/// <param name="Seconds">Their time together.</param>
public sealed record BackupTypeDayResponse(int Count, double Seconds)
{
    /// <summary>No backups.</summary>
    public static BackupTypeDayResponse None { get; } = new(0, 0);
}
