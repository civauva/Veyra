namespace Veyra.Ingestion;

public sealed class IngestionOptions
{
    /// <summary>Maximum number of records buffered before backpressure applies.</summary>
    public int MaxQueueLength { get; set; } = 10_000;
}
