namespace Veyra.Core.Telemetry;

/// <summary>
/// The disk space stored logs use, and what limits it.
/// </summary>
/// <param name="UsedBytes">All log partitions together, indexes included.</param>
/// <param name="BudgetBytes">The configured storage budget, or <see langword="null"/> when none is set.</param>
/// <param name="RetentionDays">How many whole days of logs are kept before today.</param>
/// <param name="DailyBytes">
/// The average size of one full day over the last seven days, or <see langword="null"/> before the first full day.
/// </param>
public sealed record LogStorageUsage(long UsedBytes, long? BudgetBytes, int RetentionDays, long? DailyBytes);
