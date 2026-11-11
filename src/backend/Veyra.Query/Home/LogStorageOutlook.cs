using Veyra.Core.Telemetry;

namespace Veyra.Query.Home;

/// <summary>
/// Projects when log storage reaches its budget, for the outlook on the Forecast home.
/// </summary>
internal static class LogStorageOutlook
{
    /// <summary>Builds the storage part of the snapshot from a measurement.</summary>
    /// <remarks>
    /// Retention keeps the configured number of whole days plus today, so storage settles at about that many days
    /// of growth. When that steady size fits the budget, the budget is never reached. Otherwise the remaining space
    /// is divided by the daily growth.
    /// </remarks>
    /// <param name="usage">The measurement from the Storage module.</param>
    /// <returns>The size, the budget, and the projection.</returns>
    public static LogStorageResponse From(LogStorageUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        int? days = null;
        bool? reaches = null;

        if (usage is { BudgetBytes: { } budget, DailyBytes: { } daily } && daily > 0)
        {
            var steady = daily * (usage.RetentionDays + 1L);

            reaches = steady > budget;

            if (reaches.Value)
            {
                var left = Math.Max(0, budget - usage.UsedBytes);
                days = (int)Math.Ceiling((double)left / daily);
            }
        }

        return new LogStorageResponse(
            usage.UsedBytes,
            usage.BudgetBytes,
            usage.DailyBytes,
            usage.RetentionDays,
            days,
            reaches);
    }
}
