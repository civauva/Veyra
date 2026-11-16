using System.IO.Compression;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpAmp.Proto.V1;

namespace Veyra.Agents.OpAmp;

/// <summary>
/// <c>POST /v1/opamp</c>: OpAMP over plain HTTP, as the OpAMP specification defines it. Agents poll it; each poll is an
/// <c>AgentToServer</c> message answered by a <c>ServerToAgent</c> message, both protobuf.
/// </summary>
/// <remarks>
/// The agent signs every poll with its ingestion key in <c>Authorization: Bearer</c>. It is open to anonymous callers
/// because it checks that key itself, like the OTLP endpoints.
/// </remarks>
internal static class OpAmpEndpoint
{
    /// <summary>The endpoint's path, which the agent's start-up file names.</summary>
    public const string Path = "/v1/opamp";

    private const string _protobuf = "application/x-protobuf";

    // A poll carries the agent's state and, at most, its effective configuration: a few dozen kilobytes.
    private const int _maxBodyBytes = 1024 * 1024;

    /// <summary>Maps the endpoint.</summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, HandleAsync)
            .AllowAnonymous()
            .WithMetadata(new RequestSizeLimitAttribute(_maxBodyBytes))
            .WithName("OpAmp");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        OpAmpServer server,
        CancellationToken cancellationToken)
    {
        var mediaType = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var parsed)
            ? parsed.MediaType
            : null;

        if (mediaType != _protobuf)
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (await ReadBodyAsync(context.Request, cancellationToken) is not { } body)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        AgentToServer message;

        try
        {
            message = AgentToServer.Parser.ParseFrom(body);
        }
        catch (InvalidProtocolBufferException)
        {
            return Results.BadRequest();
        }

        // The agent's Collector sends to the address the agent used, so a built-in agent stays inside the Compose
        // network and a remote one goes through the reverse proxy, which the forwarded headers describe.
        var veyraUrl = new Uri($"{context.Request.Scheme}://{context.Request.Host}");
        var reply = await server.HandleAsync(BearerToken(context.Request), message, veyraUrl, cancellationToken);

        if (reply is null)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";

            return Results.Unauthorized();
        }

        return Results.Bytes(reply.ToByteArray(), _protobuf);
    }

    private static string? BearerToken(HttpRequest request) =>
        AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            ? header.Parameter
            : null;

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
