using OpenTelemetry.Proto.Collector.Trace.V1;
using Veyra.Core.Security;
using Veyra.Core.Traces;
using Veyra.Ingestion.Otlp;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// Accepts an OTLP trace request: maps its spans, keeps those the ingestion key may send, and queues them. A batch is
/// queued as it arrives; nothing waits for the rest of a trace (ADR 0014).
/// </summary>
/// <param name="queue">The bounded queue to the span writer.</param>
internal sealed class SpanPipeline(SpanQueue queue)
{
    /// <summary>Maps, filters, and queues the spans of a request.</summary>
    /// <param name="request">The OTLP request.</param>
    /// <param name="key">The checked ingestion key, with the environments it may send for.</param>
    /// <returns>How many spans were accepted and rejected, and why; or that the queue is full.</returns>
    public IngestResult Accept(ExportTraceServiceRequest request, IngestionKeyCheck key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var mapped = OtlpSpanMapper.Map(request);
        var accepted = mapped.Spans;
        var reasons = new List<string>();

        if (mapped.Invalid > 0)
        {
            reasons.Add($"{mapped.Invalid} spans without a trace ID, a span ID, or a start time were left out.");
        }

        if (key.AllowedEnvironments is { } allowed)
        {
            var kept = new List<Span>(accepted.Count);
            var refused = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var span in accepted)
            {
                if (span.Environment is not null && allowed.Contains(span.Environment))
                {
                    kept.Add(span);
                }
                else
                {
                    refused.Add(span.Environment ?? "(none)");
                }
            }

            if (refused.Count > 0)
            {
                reasons.Add($"This ingestion key may not send data for environment {string.Join(", ", refused)}.");
            }

            accepted = kept;
        }

        if (!queue.TryEnqueue(accepted))
        {
            return new IngestResult(0, 0, null, QueueFull: true);
        }

        return new IngestResult(
            accepted.Count,
            mapped.Invalid + (mapped.Spans.Count - accepted.Count),
            reasons.Count > 0 ? string.Join(" ", reasons) : null,
            QueueFull: false);
    }
}
