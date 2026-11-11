namespace Veyra.Query.Home;

/// <summary>
/// How much space logs use, and whether and when they will reach the storage budget.
/// </summary>
/// <param name="UsedBytes">All stored logs, indexes included.</param>
/// <param name="BudgetBytes">The configured budget, or null when none is set.</param>
/// <param name="DailyBytes">The average size of a full day, or null before the first full day.</param>
/// <param name="RetentionDays">How many whole days are kept before today.</param>
/// <param name="DaysUntilBudget">Days until the budget is reached, or null when it is not reached or not known.</param>
/// <param name="ReachesBudget">
/// Whether the budget will be reached before retention removes old days; null without a budget or a daily average.
/// </param>
public sealed record LogStorageResponse(
    long UsedBytes,
    long? BudgetBytes,
    long? DailyBytes,
    int RetentionDays,
    int? DaysUntilBudget,
    bool? ReachesBudget);
