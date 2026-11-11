using OpenTelemetry.Proto.Collector.Metrics.V1;
using Veyra.Core.Metrics;
using Veyra.Core.Security;
using Veyra.Ingestion.Otlp;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The part of metrics ingestion that both OTLP transports share: map the request, apply the key's environment limit,
/// and queue the result.
/// </summary>
/// <param name="queue">The bounded queue to the metric writer.</param>
/// <param name="clock">The clock for points that carry no time.</param>
internal sealed class MetricPipeline(MetricQueue queue, TimeProvider clock)
{
    /// <summary>Accepts the points of one request from a sender whose key was already checked.</summary>
    /// <param name="request">The decoded OTLP request.</param>
    /// <param name="key">The result of checking the sender's ingestion key. It must be valid.</param>
    /// <returns>How many points were queued or refused, or that Veyra is too busy right now.</returns>
    public IngestResult Accept(ExportMetricsServiceRequest request, IngestionKeyCheck key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var mapped = OtlpMetricMapper.Map(request, clock.GetUtcNow());
        var accepted = mapped.Points;
        var reasons = new List<string>();

        if (mapped.RefusalReason is { } unsupported)
        {
            reasons.Add(unsupported);
        }

        if (key.AllowedEnvironments is { } allowed)
        {
            var kept = new List<MetricPoint>(accepted.Count);
            var refused = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var point in accepted)
            {
                if (point.Environment is not null && allowed.Contains(point.Environment))
                {
                    kept.Add(point);
                }
                else
                {
                    refused.Add(point.Environment ?? "(none)");
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

        var rejected = mapped.Refused + (mapped.Points.Count - accepted.Count);

        return new IngestResult(
            accepted.Count,
            rejected,
            reasons.Count > 0 ? string.Join(" ", reasons) : null,
            QueueFull: false);
    }
}
