namespace Veyra.Core.Activity;

/// <summary>
/// The backups of one type that one database had on one day, in UTC (ADR 0025). The Backups tab draws its duration per
/// day and its 7-day grid from these, so it never has to read every log backup of a month.
/// </summary>
/// <param name="Day">The day the backups finished, in UTC.</param>
/// <param name="Database">The database.</param>
/// <param name="Type">The type as the agent names it: <c>full</c>, <c>differential</c>, or <c>log</c>.</param>
/// <param name="Count">How many finished that day.</param>
/// <param name="Seconds">How long they took together, in seconds.</param>
public sealed record BackupDay(DateOnly Day, string Database, string Type, int Count, double Seconds);
