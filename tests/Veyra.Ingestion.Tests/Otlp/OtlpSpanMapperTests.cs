using OpenTelemetry.Proto.Trace.V1;
using Veyra.Core.Traces;
using Veyra.Ingestion.Otlp;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;
using static Veyra.Ingestion.Tests.Otlp.OtlpTraceRequests;
using Span = OpenTelemetry.Proto.Trace.V1.Span;

namespace Veyra.Ingestion.Tests.Otlp;

public class OtlpSpanMapperTests
{
    [Fact]
    public void Map_RootSpan_KeepsIdsTimesStatusAndResource()
    {
        var span = OneSpan("00F067AA0BA902B7");
        span.Status = new Status { Code = Status.Types.StatusCode.Error, Message = "PSP timed out" };

        var mapped = Assert.Single(OtlpSpanMapper.Map(Spans("payments", "production", span)).Spans);

        Assert.Equal(
            (TraceId, "00f067aa0ba902b7", (string?)null),
            (mapped.TraceId, mapped.SpanId, mapped.ParentSpanId));
        Assert.Equal(("POST /payments", SpanKind.Server), (mapped.Name, mapped.Kind));
        Assert.Equal(Noon, mapped.Start);
        Assert.Equal(TimeSpan.FromMicroseconds(1_234_567), mapped.Duration);
        Assert.Equal((SpanStatus.Error, "PSP timed out"), (mapped.Status, mapped.StatusMessage));
        Assert.Equal(("payments", "production", "web-02"), (mapped.ServiceName, mapped.Environment, mapped.HostName));
        Assert.Equal("Payments.Api", mapped.Attributes["otel.scope.name"]);
    }

    [Fact]
    public void Map_ChildSpan_KeepsItsParent()
    {
        var request = Spans("payments", null, OneSpan("0000000000000002", "0000000000000001"));

        var mapped = Assert.Single(OtlpSpanMapper.Map(request).Spans);

        Assert.Equal("0000000000000001", mapped.ParentSpanId);
    }

    [Fact]
    public void Map_ExceptionEvent_KeepsItsTimeAndAttributes()
    {
        var span = OneSpan("0000000000000001");
        var exception = new Span.Types.Event { Name = "exception", TimeUnixNano = UnixNanos(Noon.AddSeconds(1)) };
        exception.Attributes.Add(Attribute("exception.type", "System.TimeoutException"));
        exception.Attributes.Add(Attribute("exception.message", "The PSP did not answer"));
        span.Events.Add(exception);

        var mapped = Assert.Single(OtlpSpanMapper.Map(Spans("payments", null, span)).Spans);

        var @event = Assert.Single(mapped.Events);
        Assert.Equal(("exception", Noon.AddSeconds(1)), (@event.Name, @event.Timestamp));
        Assert.Equal("System.TimeoutException", @event.Attributes["exception.type"]);
    }

    [Fact]
    public void Map_TooManyAttributesEventsAndLinks_KeepsTheLimitsAndCountsTheRest()
    {
        var span = OneSpan("0000000000000001");
        span.DroppedAttributesCount = 3;

        for (var i = 0; i < 200; i++)
        {
            span.Attributes.Add(Attribute($"key.{i:D3}", "value"));
            span.Events.Add(new Span.Types.Event { Name = $"event {i}", TimeUnixNano = UnixNanos(Noon) });
        }

        for (var i = 0; i < 40; i++)
        {
            span.Links.Add(new Span.Types.Link { TraceId = Bytes(TraceId), SpanId = Bytes($"{i + 1:X16}") });
        }

        var mapped = Assert.Single(OtlpSpanMapper.Map(Spans("payments", null, span)).Spans);

        // 128 attributes in all, the scope name among them; ADR 0014 sets the limits.
        Assert.Equal(128, mapped.Attributes.Count);
        Assert.Equal(128, mapped.Events.Count);
        Assert.Equal(32, mapped.Links.Count);
        Assert.Equal(new SpanDropped(200 + 1 - 128 + 3, 200 - 128, 40 - 32), mapped.Dropped);
    }

    [Fact]
    public void Map_LongValues_AreCutAt4KbAndStackTracesAt32Kb()
    {
        var span = OneSpan("0000000000000001");
        span.Attributes.Add(Attribute("db.statement", new string('s', 10_000)));
        var exception = new Span.Types.Event { Name = "exception", TimeUnixNano = UnixNanos(Noon) };
        exception.Attributes.Add(Attribute("exception.stacktrace", new string('t', 50_000)));
        span.Events.Add(exception);

        var mapped = Assert.Single(OtlpSpanMapper.Map(Spans("payments", null, span)).Spans);

        Assert.Equal(4096, mapped.Attributes["db.statement"].Length);
        Assert.Equal(32_768, mapped.Events[0].Attributes["exception.stacktrace"].Length);
    }

    [Fact]
    public void Map_SpanWithoutIdsOrStart_IsLeftOutAndCounted()
    {
        var noTrace = OneSpan("0000000000000001");
        noTrace.TraceId = Bytes("00000000000000000000000000000000");
        var noSpan = OneSpan("0000000000000000");
        var noStart = OneSpan("0000000000000003");
        noStart.StartTimeUnixNano = 0;

        var mapped = OtlpSpanMapper.Map(Spans("payments", null, noTrace, noSpan, noStart, OneSpan("0000000000000004")));

        Assert.Equal("0000000000000004", Assert.Single(mapped.Spans).SpanId);
        Assert.Equal(3, mapped.Invalid);
    }

    [Fact]
    public void Map_EndBeforeStart_HasNoDuration()
    {
        var span = OneSpan("0000000000000001");
        span.EndTimeUnixNano = span.StartTimeUnixNano - 1_000;

        Assert.Equal(TimeSpan.Zero, Assert.Single(OtlpSpanMapper.Map(Spans("payments", null, span)).Spans).Duration);
    }
}
