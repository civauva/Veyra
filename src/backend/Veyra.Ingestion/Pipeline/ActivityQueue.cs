using Microsoft.Extensions.Options;
using Veyra.Core.Activity;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The bounded buffer between the OTLP logs endpoints and the database activity writer (ADR 0020), limited to
/// <see cref="IngestionOptions.MaxQueueLength"/> records like the log queue.
/// </summary>
/// <param name="options">The Ingestion settings, for the limit.</param>
internal sealed class ActivityQueue(IOptionsMonitor<IngestionOptions> options)
    : BoundedBatchQueue<DatabaseActivity>(() => options.CurrentValue.MaxQueueLength)
{
    /// <summary>The number of records accepted but not yet written.</summary>
    public int QueuedRecords => Queued;
}
