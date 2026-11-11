using Microsoft.Extensions.Options;
using Veyra.Core.Metrics;

namespace Veyra.Ingestion.Pipeline;

/// <summary>
/// The bounded buffer between the OTLP metrics endpoints and the metric writer, limited to
/// <see cref="IngestionOptions.MaxMetricQueueLength"/> points.
/// </summary>
/// <param name="options">The Ingestion settings, for the limit.</param>
internal sealed class MetricQueue(IOptions<IngestionOptions> options)
    : BoundedBatchQueue<MetricPoint>(options.Value.MaxMetricQueueLength);
