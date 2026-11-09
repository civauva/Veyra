using System.IO.Compression;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry.Proto.Collector.Logs.V1;
using Veyra.Core.Security;
using Veyra.Ingestion.Pipeline;

namespace Veyra.Ingestion.Otlp;

/// <summary>
/// The OTLP/HTTP logs endpoint, <c>POST /v1/logs</c>, as the OTLP specification defines it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// Bodies are protobuf (<c>application/x-protobuf</c>) or JSON (<c>application/json</c>), optionally gzipped.
/// </item>
/// <item>The sender's ingestion key comes in <c>Authorization: Bearer</c> (ADR 0008). Without a valid key: 401.</item>
/// <item>After repeated wrong keys from one address: 429 for the rest of the minute.</item>
/// <item>When the queue is full: 503 with <c>Retry-After</c>, so the SDK or Collector retries.</item>
/// <item>Refused records, for example for a disallowed environment, are reported as partial success.</item>
/// </list>
/// </remarks>
internal static class OtlpHttpEndpoint
{
    private const string _protobuf = "application/x-protobuf";

    private const string _json = "application/json";

    // OTLP bodies are batches; the SDKs keep them far below this. The same limit applies after decompression, so a
    // small gzip bomb cannot expand into gigabytes.
    private const int _maxBodyBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Maps <c>POST /v1/logs</c>. It is open to anonymous callers because it checks ingestion keys itself.
    /// </summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    /// <returns>The endpoint's convention builder.</returns>
    public static IEndpointConventionBuilder Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/v1/logs", HandleAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(_maxBodyBytes))
            .WithName("OtlpHttpLogs");

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        IIngestionKeyValidator keys,
        FailedKeyThrottle throttle,
        LogPipeline pipeline,
        CancellationToken cancellationToken)
    {
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (throttle.IsBlocked(client))
        {
            context.Response.Headers.RetryAfter = "60";

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        var key = await keys.ValidateAsync(BearerToken(context.Request), cancellationToken);

        if (!key.IsValid)
        {
            throttle.RecordFailure(client);
            context.Response.Headers.WWWAuthenticate = "Bearer";

            return Results.Unauthorized();
        }

        var mediaType = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var parsed)
            ? parsed.MediaType
            : null;

        if (mediaType is not (_protobuf or _json))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var body = await ReadBodyAsync(context.Request, cancellationToken);

        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        ExportLogsServiceRequest request;

        try
        {
            request = mediaType == _json
                ? OtlpJson.ParseLogsRequest(System.Text.Encoding.UTF8.GetString(body))
                : ExportLogsServiceRequest.Parser.ParseFrom(body);
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or OtlpFormatException)
        {
            return Results.BadRequest();
        }

        var result = pipeline.Accept(request, key);

        if (result.QueueFull)
        {
            // OTLP senders treat 503 as retryable and honour Retry-After.
            context.Response.Headers.RetryAfter = "1";

            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var response = new ExportLogsServiceResponse();

        if (result.Rejected > 0)
        {
            response.PartialSuccess = new ExportLogsPartialSuccess
            {
                RejectedLogRecords = result.Rejected,
                ErrorMessage = result.RejectionReason ?? string.Empty,
            };
        }

        return mediaType == _json
            ? Results.Text(OtlpJson.Format(response), _json)
            : Results.Bytes(response.ToByteArray(), _protobuf);
    }

    private static string BearerToken(HttpRequest request) =>
        AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter ?? string.Empty
            : string.Empty;

    /// <summary>Reads the body, decompressing gzip, and stops at the size limit.</summary>
    /// <returns>The bytes, or <see langword="null"/> when the body is larger than the limit.</returns>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var gzip = string.Equals(request.Headers.ContentEncoding, "gzip", StringComparison.OrdinalIgnoreCase);
        await using var source = gzip ? new GZipStream(request.Body, CompressionMode.Decompress) : request.Body;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > _maxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
