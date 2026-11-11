using System.Net.Http.Headers;
using Grpc.Core;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>The checks every OTLP/gRPC service makes before it accepts data, and its answers to the pipeline.</summary>
internal static class OtlpGrpcAccess
{
    /// <summary>
    /// Checks the ingestion key in the <c>authorization</c> metadata, after the wrong-key throttle.
    /// </summary>
    /// <param name="context">The call.</param>
    /// <param name="keys">Checks ingestion keys.</param>
    /// <param name="throttle">Slows down clients that keep presenting wrong keys.</param>
    /// <returns>The valid key.</returns>
    /// <exception cref="RpcException">The client is throttled, or the key is missing or wrong.</exception>
    public static async Task<IngestionKeyCheck> CheckKeyAsync(
        ServerCallContext context,
        IIngestionKeyValidator keys,
        FailedKeyThrottle throttle)
    {
        ArgumentNullException.ThrowIfNull(context);

        var client = context.GetHttpContext().Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (throttle.IsBlocked(client))
        {
            throw new RpcException(
                new Status(StatusCode.ResourceExhausted, "Too many wrong ingestion keys. Wait a minute."));
        }

        var key = await keys.ValidateAsync(BearerToken(context.RequestHeaders), context.CancellationToken);

        if (!key.IsValid)
        {
            throttle.RecordFailure(client);

            throw new RpcException(new Status(StatusCode.Unauthenticated, "A valid ingestion key is required."));
        }

        return key;
    }

    /// <summary>Turns a full queue into <c>UNAVAILABLE</c>, which OTLP senders retry.</summary>
    /// <param name="result">What the pipeline did.</param>
    /// <exception cref="RpcException">The queue was full.</exception>
    public static void ThrowWhenBusy(IngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.QueueFull)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Veyra is busy. Retry shortly."));
        }
    }

    private static string BearerToken(Metadata headers) =>
        AuthenticationHeaderValue.TryParse(headers.GetValue("authorization"), out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter ?? string.Empty
            : string.Empty;
}
