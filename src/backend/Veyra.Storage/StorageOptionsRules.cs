using Veyra.Core.Settings;

namespace Veyra.Storage;

/// <summary>
/// What <see cref="StorageOptions"/> must meet, at startup and before the Settings screen saves a change.
/// </summary>
public sealed class StorageOptionsRules : OptionsRules<StorageOptions>
{
    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        yield return (options.LogRetentionDays >= 1, "Veyra:Storage:LogRetentionDays must be at least 1.");
        yield return (
            options.MaxLogStorageGigabytes is null or > 0,
            "Veyra:Storage:MaxLogStorageGigabytes must be positive when set.");
        yield return (options.MetricRetentionDays >= 1, "Veyra:Storage:MetricRetentionDays must be at least 1.");
        yield return (options.MaxMetricSeries >= 1, "Veyra:Storage:MaxMetricSeries must be at least 1.");
        yield return (options.TraceRetentionDays >= 1, "Veyra:Storage:TraceRetentionDays must be at least 1.");
        yield return (
            options.DatabaseSessionRetentionDays >= 1,
            "Veyra:Storage:DatabaseSessionRetentionDays must be at least 1.");
        yield return (
            options.DatabaseRecordRetentionDays >= 1,
            "Veyra:Storage:DatabaseRecordRetentionDays must be at least 1.");
        yield return (
            options.MaxTraceStorageGigabytes is null or > 0,
            "Veyra:Storage:MaxTraceStorageGigabytes must be positive when set.");
    }
}
