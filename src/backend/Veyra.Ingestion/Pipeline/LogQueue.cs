using Microsoft.Extensions.Options;
using Veyra.Core.Telemetry;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The bounded buffer between the OTLP logs endpoints and the log writer, limited to
/// <see cref="IngestionOptions.MaxQueueLength"/> records.
/// </summary>
/// <param name="options">The Ingestion settings, for the limit.</param>
internal sealed class LogQueue(IOptions<IngestionOptions> options)
    : BoundedBatchQueue<LogRecord>(options.Value.MaxQueueLength)
{
    /// <summary>The number of records accepted but not yet written.</summary>
    public int QueuedRecords => Queued;
}
