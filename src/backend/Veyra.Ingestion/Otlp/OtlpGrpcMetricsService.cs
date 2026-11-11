using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// The OTLP/gRPC metrics service, <c>opentelemetry.proto.collector.metrics.v1.MetricsService/Export</c>, with the
/// same rules as the logs service.
/// </summary>
/// <param name="keys">Checks ingestion keys.</param>
/// <param name="throttle">Slows down clients that keep presenting wrong keys.</param>
/// <param name="pipeline">Maps and queues the points.</param>
internal sealed class OtlpGrpcMetricsService(
    IIngestionKeyValidator keys,
    FailedKeyThrottle throttle,
    MetricPipeline pipeline)
    : MetricsService.MetricsServiceBase
{
    /// <summary>Receives one batch of metric points.</summary>
    /// <param name="request">The points.</param>
    /// <param name="context">The call, which carries the metadata with the key.</param>
    /// <returns>An empty response, or partial success when points were refused.</returns>
    /// <exception cref="RpcException">The key is missing or wrong, or Veyra is too busy right now.</exception>
    public override async Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        var key = await OtlpGrpcAccess.CheckKeyAsync(context, keys, throttle);
        var result = pipeline.Accept(request, key);

        OtlpGrpcAccess.ThrowWhenBusy(result);

        return OtlpResponses.Metrics(result);
    }
}
