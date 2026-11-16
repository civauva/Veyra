using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;
using Veyra.Testing;
using static Veyra.Ingestion.Tests.Otlp.OtlpTraceRequests;

namespace Veyra.Ingestion.Tests.Pipeline;

public class SpanPipelineTests
{
    private static readonly IngestionKeyCheck _anyEnvironment = new(true, Guid.NewGuid(), null);

    [Fact]
    public void Accept_KeyForAllEnvironments_QueuesEverySpan()
    {
        var queue = Queue(capacity: 10);

        var result = new SpanPipeline(queue).Accept(
            Spans("payments", "production", OneSpan("0000000000000001"), OneSpan("0000000000000002")),
            _anyEnvironment);

        Assert.Equal(new IngestResult(2, 0, null, QueueFull: false), result);
        Assert.Equal(2, queue.QueuedSpans);
    }

    [Fact]
    public void Accept_KeyLimitedToOtherEnvironment_RejectsItsSpansWithAReason()
    {
        var queue = Queue(capacity: 10);
        var request = Spans("payments", "production", OneSpan("0000000000000001"));
        request.ResourceSpans.Add(Spans("payments", "staging", OneSpan("0000000000000002")).ResourceSpans[0]);

        var result = new SpanPipeline(queue).Accept(request, new IngestionKeyCheck(true, Guid.NewGuid(), ["staging"]));

        Assert.Equal((1, 1), (result.Accepted, result.Rejected));
        Assert.Contains("production", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_InvalidSpans_AreRejectedWithAReason()
    {
        var invalid = OneSpan("0000000000000000");

        var result = new SpanPipeline(Queue(capacity: 10)).Accept(
            Spans("payments", null, invalid, OneSpan("0000000000000001")),
            _anyEnvironment);

        Assert.Equal((1, 1), (result.Accepted, result.Rejected));
        Assert.Contains("ID", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_QueueFull_RejectsTheWholeRequestSoTheSenderRetries()
    {
        var queue = Queue(capacity: 2);
        var pipeline = new SpanPipeline(queue);
        pipeline.Accept(
            Spans("payments", null, OneSpan("0000000000000001"), OneSpan("0000000000000002")),
            _anyEnvironment);

        var result = pipeline.Accept(Spans("payments", null, OneSpan("0000000000000003")), _anyEnvironment);

        Assert.True(result.QueueFull);
        Assert.Equal(2, queue.QueuedSpans);
    }

    private static SpanQueue Queue(int capacity) =>
        new(new TestOptionsMonitor<IngestionOptions>(new IngestionOptions { MaxSpanQueueLength = capacity }));
}
