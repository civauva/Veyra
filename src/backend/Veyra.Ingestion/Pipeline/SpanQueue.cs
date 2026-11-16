using Microsoft.Extensions.Options;
using Veyra.Core.Traces;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The bounded queue between the OTLP trace endpoints and the span writer (ADR 0014). It has its own limit, so a burst
/// of spans cannot hold back logs or metrics.
/// </summary>
/// <param name="options">The ingestion options with the queue's limit.</param>
internal sealed class SpanQueue(IOptionsMonitor<IngestionOptions> options)
    : BoundedBatchQueue<Span>(() => options.CurrentValue.MaxSpanQueueLength)
{
    /// <summary>How many spans wait to be written.</summary>
    public int QueuedSpans => Queued;
}
