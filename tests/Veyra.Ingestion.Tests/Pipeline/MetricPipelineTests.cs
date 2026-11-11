using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Metrics.V1;
using Veyra.Core.Security;
using Veyra.Ingestion.Otlp;
using Veyra.Ingestion.Pipeline;
using static Veyra.Ingestion.Tests.Otlp.OtlpMetricRequests;

namespace Veyra.Ingestion.Tests.Pipeline;

public class MetricPipelineTests
{
    private static readonly IngestionKeyCheck _anyEnvironment = new(true, Guid.NewGuid(), null);

    [Fact]
    public void Accept_KeyForAllEnvironments_QueuesEveryPoint()
    {
        var queue = Queue(capacity: 10);

        var result = new MetricPipeline(queue, TimeProvider.System).Accept(
            Metrics(ServiceResource("api"), Gauge("a", 1), Counter("b", 2)),
            _anyEnvironment);

        Assert.Equal(new IngestResult(2, 0, null, QueueFull: false), result);
        Assert.Equal(2, queue.Queued);
    }

    [Fact]
    public void Accept_KeyLimitedToOtherEnvironment_RefusesItsPointsWithAReason()
    {
        var queue = Queue(capacity: 10);
        var stagingOnly = new IngestionKeyCheck(true, Guid.NewGuid(), ["staging"]);

        var result = new MetricPipeline(queue, TimeProvider.System).Accept(
            Metrics(ServiceResource("api"), Gauge("a", 1)),
            stagingOnly);

        Assert.Equal((0, 1), (result.Accepted, result.Rejected));
        Assert.Contains("production", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_UnsupportedKinds_AreCountedAsRefusedNextToTheAcceptedOnes()
    {
        var result = new MetricPipeline(Queue(capacity: 10), TimeProvider.System).Accept(
            Metrics(ServiceResource("api"), Gauge("a", 1), Durations("d", AggregationTemporality.Delta)),
            _anyEnvironment);

        Assert.Equal((1, 1), (result.Accepted, result.Rejected));
        Assert.Equal(OtlpMetricMapper.UnsupportedReason, result.RejectionReason);
    }

    [Fact]
    public void Accept_QueueFull_QueuesNothingAndSaysSo()
    {
        var queue = Queue(capacity: 1);

        var result = new MetricPipeline(queue, TimeProvider.System).Accept(
            Metrics(ServiceResource("api"), Gauge("a", 1), Gauge("b", 2)),
            _anyEnvironment);

        Assert.True(result.QueueFull);
        Assert.Equal(0, queue.Queued);
    }

    private static MetricQueue Queue(int capacity) =>
        new(Options.Create(new IngestionOptions { MaxMetricQueueLength = capacity }));
}
