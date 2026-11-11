using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Resource.V1;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Otlp;

/// <summary>Builds OTLP metrics export requests the way the SDKs and the Collector send them.</summary>
internal static class OtlpMetricRequests
{
    /// <summary>A request with one resource and one scope holding the given metrics.</summary>
    public static ExportMetricsServiceRequest Metrics(
        IEnumerable<KeyValue> resourceAttributes,
        params Metric[] metrics)
    {
        var resource = new Resource();
        resource.Attributes.AddRange(resourceAttributes);

        var scope = new ScopeMetrics { Scope = new InstrumentationScope { Name = "System.Runtime" } };
        scope.Metrics.AddRange(metrics);

        var resourceMetrics = new ResourceMetrics { Resource = resource };
        resourceMetrics.ScopeMetrics.Add(scope);

        var request = new ExportMetricsServiceRequest();
        request.ResourceMetrics.Add(resourceMetrics);

        return request;
    }

    /// <summary>The resource of a service in an environment on a host.</summary>
    public static KeyValue[] ServiceResource(string service, string? environment = "production", string host = "web-01")
    {
        var attributes = new List<KeyValue> { Attribute("service.name", service), Attribute("host.name", host) };

        if (environment is not null)
        {
            attributes.Add(Attribute("deployment.environment.name", environment));
        }

        return [.. attributes];
    }

    /// <summary>A gauge with one point at <see cref="OtlpRequests.Noon"/>.</summary>
    public static Metric Gauge(string name, double value, params KeyValue[] attributes)
    {
        var point = new NumberDataPoint { TimeUnixNano = UnixNanos(Noon), AsDouble = value };
        point.Attributes.AddRange(attributes);

        var gauge = new Gauge();
        gauge.DataPoints.Add(point);

        return new Metric { Name = name, Unit = "1", Description = "A gauge", Gauge = gauge };
    }

    /// <summary>A cumulative monotonic sum with one integer point that started an hour before noon.</summary>
    public static Metric Counter(string name, long value, params KeyValue[] attributes)
    {
        var point = new NumberDataPoint
        {
            StartTimeUnixNano = UnixNanos(Noon.AddHours(-1)),
            TimeUnixNano = UnixNanos(Noon),
            AsInt = value,
        };
        point.Attributes.AddRange(attributes);

        var sum = new Sum
        {
            AggregationTemporality = AggregationTemporality.Cumulative,
            IsMonotonic = true,
        };
        sum.DataPoints.Add(point);

        return new Metric { Name = name, Unit = "s", Sum = sum };
    }

    /// <summary>A histogram with one point: 10 values in three buckets split at 0.1 s and 1 s.</summary>
    public static Metric Durations(
        string name,
        AggregationTemporality temporality = AggregationTemporality.Cumulative)
    {
        var point = new HistogramDataPoint
        {
            StartTimeUnixNano = UnixNanos(Noon.AddHours(-1)),
            TimeUnixNano = UnixNanos(Noon),
            Count = 10,
            Sum = 3.5,
            Min = 0.01,
            Max = 2,
        };
        point.ExplicitBounds.AddRange([0.1, 1]);
        point.BucketCounts.AddRange([6, 3, 1]);

        var histogram = new Histogram { AggregationTemporality = temporality };
        histogram.DataPoints.Add(point);

        return new Metric { Name = name, Unit = "s", Histogram = histogram };
    }
}
