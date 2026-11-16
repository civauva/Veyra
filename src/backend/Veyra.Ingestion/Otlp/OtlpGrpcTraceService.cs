using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// The OTLP/gRPC trace service, <c>opentelemetry.proto.collector.trace.v1.TraceService/Export</c> (ADR 0014).
/// </summary>
/// <remarks>
/// The same rules as OTLP/HTTP apply: the ingestion key comes in the <c>authorization</c> metadata as
/// <c>Bearer &lt;key&gt;</c>; a missing or wrong key is <c>UNAUTHENTICATED</c>; a full queue is <c>UNAVAILABLE</c>,
/// which OTLP senders retry; refused spans are reported as partial success.
/// </remarks>
/// <param name="keys">Checks ingestion keys.</param>
/// <param name="throttle">Slows down clients that keep presenting wrong keys.</param>
/// <param name="pipeline">Maps and queues the spans.</param>
internal sealed class OtlpGrpcTraceService(
    IIngestionKeyValidator keys,
    FailedKeyThrottle throttle,
    SpanPipeline pipeline)
    : TraceService.TraceServiceBase
{
    /// <summary>Receives one batch of spans.</summary>
    /// <param name="request">The spans.</param>
    /// <param name="context">The call, which carries the metadata with the key.</param>
    /// <returns>An empty response, or partial success when spans were refused.</returns>
    /// <exception cref="RpcException">The key is missing or wrong, or Veyra is too busy right now.</exception>
    public override async Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context)
    {
        var key = await OtlpGrpcAccess.CheckKeyAsync(context, keys, throttle);
        var result = pipeline.Accept(request, key);

        OtlpGrpcAccess.ThrowWhenBusy(result);

        return OtlpResponses.Traces(result);
    }
}
