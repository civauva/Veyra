using Veyra.Core.Settings;

namespace Veyra.Ingestion;

/// <summary>
/// What <see cref="IngestionOptions"/> must meet, at startup and before the Settings screen saves a change.
/// </summary>
public sealed class IngestionOptionsRules : OptionsRules<IngestionOptions>
{
    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(IngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        yield return (options.MaxQueueLength > 0, "Veyra:Ingestion:MaxQueueLength must be greater than zero.");
        yield return (
            options.MaxMetricQueueLength > 0,
            "Veyra:Ingestion:MaxMetricQueueLength must be greater than zero.");
        yield return (options.MaxSpanQueueLength > 0, "Veyra:Ingestion:MaxSpanQueueLength must be greater than zero.");
    }
}
