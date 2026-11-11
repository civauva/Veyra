using OpenTelemetry.Proto.Metrics.V1;
using Veyra.Core.Metrics;
using Veyra.Ingestion.Otlp;
using static Veyra.Ingestion.Tests.Otlp.OtlpMetricRequests;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Otlp;

public class OtlpMetricMapperTests
{
    private static readonly DateTimeOffset _receivedAt = Noon.AddSeconds(1);

    [Fact]
    public void Map_Gauge_KeepsNameUnitValueAttributesAndResource()
    {
        var request = Metrics(ServiceResource("payments"), Gauge("process.memory", 512, Attribute("state", "used")));

        var mapped = OtlpMetricMapper.Map(request, _receivedAt);

        var point = Assert.Single(mapped.Points);
        Assert.Equal(("process.memory", MetricKind.Gauge, "1", "A gauge"), (point.Name, point.Kind, point.Unit, point.Description));
        Assert.Equal(MetricTemporality.Unspecified, point.Temporality);
        Assert.Equal(("payments", "production", "web-01"), (point.ServiceName, point.Environment, point.HostName));
        Assert.Equal("used", point.Attributes["state"]);
        Assert.Equal("web-01", point.ResourceAttributes["host.name"]);
        Assert.Equal((Noon, (DateTimeOffset?)null, 512d), (point.Timestamp, point.StartTimestamp, point.Value));
        Assert.Null(point.Histogram);
        Assert.Equal(0, mapped.Refused);
    }

    [Fact]
    public void Map_CumulativeCounter_KeepsTemporalityMonotonicStartAndIntegerValue()
    {
        var point = Assert.Single(OtlpMetricMapper.Map(Metrics(ServiceResource("api"), Counter("cpu", 42)), _receivedAt).Points);

        Assert.Equal((MetricKind.Sum, MetricTemporality.Cumulative, true), (point.Kind, point.Temporality, point.Monotonic));
        Assert.Equal((Noon.AddHours(-1), 42d), (point.StartTimestamp, point.Value));
    }

    [Fact]
    public void Map_CumulativeHistogram_KeepsBucketsCountSumMinAndMax()
    {
        var point = Assert.Single(
            OtlpMetricMapper.Map(Metrics(ServiceResource("api"), Durations("http.server.request.duration")), _receivedAt).Points);

        Assert.Equal(MetricKind.Histogram, point.Kind);
        Assert.Null(point.Value);
        var histogram = point.Histogram!;
        Assert.Equal((10L, 3.5, 0.01, 2d), (histogram.Count, histogram.Sum, histogram.Min, histogram.Max));
        Assert.Equal([0.1, 1], histogram.Bounds);
        Assert.Equal([6L, 3, 1], histogram.Counts);
    }

    [Fact]
    public void Map_CollectorHostMetrics_HaveNoServiceButAHost()
    {
        var request = Metrics([Attribute("host.name", "db-01")], Gauge("system.cpu.load_average.1m", 1.5));

        var point = Assert.Single(OtlpMetricMapper.Map(request, _receivedAt).Points);

        Assert.Equal((null, null, "db-01"), (point.ServiceName, point.Environment, point.HostName));
    }

    [Fact]
    public void Map_UnsupportedKinds_AreRefusedWithAReason()
    {
        var exponential = new Metric { Name = "exp", ExponentialHistogram = new ExponentialHistogram() };
        exponential.ExponentialHistogram.DataPoints.Add(new ExponentialHistogramDataPoint { Count = 1 });
        var summary = new Metric { Name = "sum", Summary = new Summary() };
        summary.Summary.DataPoints.Add(new SummaryDataPoint());
        summary.Summary.DataPoints.Add(new SummaryDataPoint());
        var request = Metrics(
            ServiceResource("api"),
            exponential,
            summary,
            Durations("delta", AggregationTemporality.Delta),
            Gauge("ok", 1));

        var mapped = OtlpMetricMapper.Map(request, _receivedAt);

        Assert.Equal("ok", Assert.Single(mapped.Points).Name);
        Assert.Equal(4, mapped.Refused);
        Assert.Contains("exponential histograms, summaries, and delta histograms", mapped.RefusalReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_HistogramWithWrongBucketCount_IsRefused()
    {
        var broken = Durations("broken");
        broken.Histogram.DataPoints[0].BucketCounts.RemoveAt(2);

        var mapped = OtlpMetricMapper.Map(Metrics(ServiceResource("api"), broken), _receivedAt);

        Assert.Empty(mapped.Points);
        Assert.Equal(1, mapped.Refused);
    }

    [Fact]
    public void Map_PointWithoutRecordedValue_IsSkippedQuietly()
    {
        var gauge = Gauge("gone", 0);
        gauge.Gauge.DataPoints[0].Flags = (uint)DataPointFlags.NoRecordedValueMask;

        var mapped = OtlpMetricMapper.Map(Metrics(ServiceResource("api"), gauge), _receivedAt);

        Assert.Empty(mapped.Points);
        Assert.Equal(0, mapped.Refused);
    }

    [Fact]
    public void Map_PointWithoutTime_UsesTheReceiveTime()
    {
        var gauge = Gauge("g", 1);
        gauge.Gauge.DataPoints[0].TimeUnixNano = 0;

        Assert.Equal(_receivedAt, Assert.Single(OtlpMetricMapper.Map(Metrics(ServiceResource("api"), gauge), _receivedAt).Points).Timestamp);
    }
}
