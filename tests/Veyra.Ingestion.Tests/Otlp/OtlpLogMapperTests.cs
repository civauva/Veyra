using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Otlp;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Otlp;

public class OtlpLogMapperTests
{
    private static readonly DateTimeOffset _receivedAt = Noon.AddSeconds(1);

    [Fact]
    public void Map_TypicalRecord_KeepsMessageSeverityServiceAndEnvironment()
    {
        var request = Single("payments", "production", Record("Payment declined", SeverityNumber.Error));

        var record = Assert.Single(OtlpLogMapper.Map(request, _receivedAt));

        Assert.Equal("Payment declined", record.Body);
        Assert.Equal(LogSeverity.Error, record.Severity);
        Assert.Equal("Error", record.SeverityText);
        Assert.Equal("payments", record.ServiceName);
        Assert.Equal("production", record.Environment);
        Assert.Equal(Noon, record.Timestamp);
        Assert.Equal(Noon.AddMilliseconds(5), record.ObservedTimestamp);
        Assert.Equal("payments", record.ResourceAttributes["service.name"]);
        Assert.Equal("Payments.Api", record.Attributes["otel.scope.name"]);
    }

    [Fact]
    public void Map_OlderEnvironmentAttribute_IsUsedWhenTheNewOneIsMissing()
    {
        var request = Single("payments", null, Record("x"));
        request.ResourceLogs[0].Resource.Attributes.Add(Attribute("deployment.environment", "staging"));

        Assert.Equal("staging", Assert.Single(OtlpLogMapper.Map(request, _receivedAt)).Environment);
    }

    [Fact]
    public void Map_NoServiceName_UsesUnknownService()
    {
        var request = Single("ignored", null, Record("x"));
        request.ResourceLogs[0].Resource.Attributes.Clear();

        // The OpenTelemetry specification names services without a name "unknown_service".
        Assert.Equal("unknown_service", Assert.Single(OtlpLogMapper.Map(request, _receivedAt)).ServiceName);
    }

    [Fact]
    public void Map_MissingTimestamps_FallBackToObservedThenReceivedTime()
    {
        var onlyObserved = Record("observed only");
        onlyObserved.TimeUnixNano = 0;
        var neither = Record("neither");
        neither.TimeUnixNano = 0;
        neither.ObservedTimeUnixNano = 0;

        var records = OtlpLogMapper.Map(Single("payments", null, onlyObserved, neither), _receivedAt);

        Assert.Equal(Noon.AddMilliseconds(5), records[0].Timestamp);
        Assert.Equal(_receivedAt, records[1].Timestamp);
        Assert.Equal(_receivedAt, records[1].ObservedTimestamp);
    }

    [Fact]
    public void Map_TraceAndSpanIds_BecomeLowerCaseHex()
    {
        var record = Record("traced");
        record.TraceId = Bytes("4BF92F3577B34DA6A3CE929D0E0E4736");
        record.SpanId = Bytes("00F067AA0BA902B7");

        var mapped = Assert.Single(OtlpLogMapper.Map(Single("payments", null, record), _receivedAt));

        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", mapped.TraceId);
        Assert.Equal("00f067aa0ba902b7", mapped.SpanId);
    }

    [Fact]
    public void Map_AllZeroTraceId_IsTreatedAsNoTrace()
    {
        var record = Record("not traced");
        record.TraceId = Bytes("00000000000000000000000000000000");

        Assert.Null(Assert.Single(OtlpLogMapper.Map(Single("payments", null, record), _receivedAt)).TraceId);
    }

    [Fact]
    public void Map_NonStringValues_AreStoredAsText()
    {
        var record = Record("x");
        record.Attributes.Add(new KeyValue { Key = "retry", Value = new AnyValue { IntValue = 3 } });
        record.Attributes.Add(new KeyValue { Key = "ok", Value = new AnyValue { BoolValue = true } });
        record.Attributes.Add(new KeyValue { Key = "ratio", Value = new AnyValue { DoubleValue = 0.25 } });
        var list = new ArrayValue();
        list.Values.Add(new AnyValue { StringValue = "a" });
        list.Values.Add(new AnyValue { IntValue = 1 });
        record.Body = new AnyValue { ArrayValue = list };

        var mapped = Assert.Single(OtlpLogMapper.Map(Single("payments", null, record), _receivedAt));

        Assert.Equal("3", mapped.Attributes["retry"]);
        Assert.Equal("true", mapped.Attributes["ok"]);
        Assert.Equal("0.25", mapped.Attributes["ratio"]);
        Assert.Equal("[\"a\",1]", mapped.Body);
    }
}
