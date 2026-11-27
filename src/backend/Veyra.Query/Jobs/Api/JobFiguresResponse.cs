namespace Veyra.Query.Jobs.Api;

/// <summary>What one job's runs of the window add up to.</summary>
/// <param name="Runs">How many runs started in the window.</param>
/// <param name="Failed">How many of them failed.</param>
/// <param name="SuccessRatio">The share of the finished runs that succeeded; null without any.</param>
/// <param name="MedianSeconds">The median duration of the finished runs; null without any.</param>
/// <param name="P95Seconds">The duration 95 of 100 finished runs did not exceed; null without any.</param>
/// <param name="RecordsProcessed">The records the runs processed, added up; null when none says.</param>
public sealed record JobFiguresResponse(
    int Runs,
    int Failed,
    double? SuccessRatio,
    double? MedianSeconds,
    double? P95Seconds,
    long? RecordsProcessed);
