using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using Veyra.Core.Metrics;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// Turns an OTLP metrics export request into Veyra's <see cref="MetricPoint"/>s (ADR 0010).
/// </summary>
/// <remarks>
/// Gauges, sums, and cumulative explicit-bucket histograms are kept. Exponential histograms, summaries, and delta
/// histograms are counted as refused, so the sender learns about them through OTLP partial success. Points flagged as
/// having no recorded value are skipped without counting: they only say that a series stopped.
/// </remarks>
internal static class OtlpMetricMapper
{
    /// <summary>The reason sent back when points of unsupported kinds were refused.</summary>
    public const string UnsupportedReason =
        "Veyra does not store exponential histograms, summaries, and delta histograms yet. Send gauges, sums, or "
        + "cumulative explicit-bucket histograms.";

    /// <summary>Maps every point in the request.</summary>
    /// <param name="request">The decoded OTLP request.</param>
    /// <param name="receivedAt">When Veyra received the request, for points that carry no time.</param>
    /// <returns>The points, in request order, and how many were refused.</returns>
    public static MappedMetrics Map(ExportMetricsServiceRequest request, DateTimeOffset receivedAt)
    {
        var points = new List<MetricPoint>();
        var refused = 0;

        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var resource = OtlpValues.ToDictionary(resourceMetrics.Resource?.Attributes);
            var series = new SeriesContext(
                resource,
                resource.GetValueOrDefault("service.name") is { Length: > 0 } service ? service : null,
                OtlpValues.Environment(resource),
                resource.GetValueOrDefault("host.name") is { Length: > 0 } host ? host : null,
                receivedAt);

            foreach (var metric in resourceMetrics.ScopeMetrics.SelectMany(scope => scope.Metrics))
            {
                refused += metric.DataCase switch
                {
                    Metric.DataOneofCase.Gauge => AddNumbers(
                        points,
                        series,
                        metric,
                        metric.Gauge.DataPoints,
                        MetricKind.Gauge,
                        MetricTemporality.Unspecified,
                        false),
                    Metric.DataOneofCase.Sum => AddNumbers(
                        points,
                        series,
                        metric,
                        metric.Sum.DataPoints,
                        MetricKind.Sum,
                        Temporality(metric.Sum.AggregationTemporality),
                        metric.Sum.IsMonotonic),
                    Metric.DataOneofCase.Histogram => AddHistograms(points, series, metric),
                    Metric.DataOneofCase.ExponentialHistogram => metric.ExponentialHistogram.DataPoints.Count,
                    Metric.DataOneofCase.Summary => metric.Summary.DataPoints.Count,
                    _ => 0,
                };
            }
        }

        return new MappedMetrics(points, refused, refused > 0 ? UnsupportedReason : null);
    }

    private static int AddNumbers(
        List<MetricPoint> points,
        SeriesContext series,
        Metric metric,
        IEnumerable<NumberDataPoint> dataPoints,
        MetricKind kind,
        MetricTemporality temporality,
        bool monotonic)
    {
        foreach (var point in dataPoints)
        {
            if (HasNoValue(point.Flags) || point.ValueCase == NumberDataPoint.ValueOneofCase.None)
            {
                continue;
            }

            var value = point.ValueCase == NumberDataPoint.ValueOneofCase.AsInt ? point.AsInt : point.AsDouble;

            points.Add(series.Point(
                metric,
                kind,
                temporality,
                monotonic,
                point.Attributes,
                point.TimeUnixNano,
                point.StartTimeUnixNano,
                value,
                null));
        }

        return 0;
    }

    private static int AddHistograms(List<MetricPoint> points, SeriesContext series, Metric metric)
    {
        var histogram = metric.Histogram;

        if (histogram.AggregationTemporality != AggregationTemporality.Cumulative)
        {
            return histogram.DataPoints.Count;
        }

        var refused = 0;

        foreach (var point in histogram.DataPoints)
        {
            if (HasNoValue(point.Flags))
            {
                continue;
            }

            // A histogram has one bucket more than it has bounds; anything else cannot be read.
            if (point.BucketCounts.Count != point.ExplicitBounds.Count + 1)
            {
                refused++;

                continue;
            }

            var value = new HistogramValue(
                (long)point.Count,
                point.HasSum ? point.Sum : null,
                point.HasMin ? point.Min : null,
                point.HasMax ? point.Max : null,
                [.. point.ExplicitBounds],
                [.. point.BucketCounts.Select(count => (long)count)]);

            points.Add(series.Point(
                metric,
                MetricKind.Histogram,
                MetricTemporality.Cumulative,
                false,
                point.Attributes,
                point.TimeUnixNano,
                point.StartTimeUnixNano,
                null,
                value));
        }

        return refused;
    }

    private static bool HasNoValue(uint flags) => (flags & (uint)DataPointFlags.NoRecordedValueMask) != 0;

    private static MetricTemporality Temporality(AggregationTemporality temporality) => temporality switch
    {
        AggregationTemporality.Delta => MetricTemporality.Delta,
        AggregationTemporality.Cumulative => MetricTemporality.Cumulative,
        _ => MetricTemporality.Unspecified,
    };

    /// <summary>What every point of one resource shares.</summary>
    private sealed record SeriesContext(
        IReadOnlyDictionary<string, string> Resource,
        string? Service,
        string? Environment,
        string? Host,
        DateTimeOffset ReceivedAt)
    {
        public MetricPoint Point(
            Metric metric,
            MetricKind kind,
            MetricTemporality temporality,
            bool monotonic,
            IEnumerable<KeyValue> attributes,
            ulong time,
            ulong start,
            double? value,
            HistogramValue? histogram) => new(
                Name: metric.Name,
                Kind: kind,
                Unit: metric.Unit,
                Description: metric.Description,
                Temporality: temporality,
                Monotonic: monotonic,
                ServiceName: Service,
                Environment: Environment,
                HostName: Host,
                ResourceAttributes: Resource,
                Attributes: OtlpValues.ToDictionary(attributes),
                Timestamp: OtlpValues.FromUnixNanos(time) ?? ReceivedAt,
                StartTimestamp: OtlpValues.FromUnixNanos(start),
                Value: value,
                Histogram: histogram);
    }
}
