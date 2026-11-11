using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// The OTLP/gRPC logs service, <c>opentelemetry.proto.collector.logs.v1.LogsService/Export</c>.
/// </summary>
/// <remarks>
/// The same rules as OTLP/HTTP apply: the ingestion key comes in the <c>authorization</c> metadata as
/// <c>Bearer &lt;key&gt;</c>; a missing or wrong key is <c>UNAUTHENTICATED</c>; a full queue is <c>UNAVAILABLE</c>,
/// which OTLP senders retry; refused records are reported as partial success.
/// </remarks>
/// <param name="keys">Checks ingestion keys.</param>
/// <param name="throttle">Slows down clients that keep presenting wrong keys.</param>
/// <param name="pipeline">Maps and queues the records.</param>
internal sealed class OtlpGrpcLogsService(
    IIngestionKeyValidator keys,
    FailedKeyThrottle throttle,
    LogPipeline pipeline)
    : LogsService.LogsServiceBase
{
    /// <summary>Receives one batch of log records.</summary>
    /// <param name="request">The records.</param>
    /// <param name="context">The call, which carries the metadata with the key.</param>
    /// <returns>An empty response, or partial success when records were refused.</returns>
    /// <exception cref="RpcException">The key is missing or wrong, or Veyra is too busy right now.</exception>
    public override async Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        var key = await OtlpGrpcAccess.CheckKeyAsync(context, keys, throttle);
        var result = pipeline.Accept(request, key);

        OtlpGrpcAccess.ThrowWhenBusy(result);

        return OtlpResponses.Logs(result);
    }
}
