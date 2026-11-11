namespace Veyra.Ingestion;

/// <summary>
/// Settings of the Ingestion module, bound from the <c>Veyra:Ingestion</c> section.
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>Maximum number of log records buffered before backpressure applies.</summary>
    public int MaxQueueLength { get; set; } = 10_000;

    /// <summary>Maximum number of metric points buffered before backpressure applies.</summary>
    public int MaxMetricQueueLength { get; set; } = 50_000;
}
