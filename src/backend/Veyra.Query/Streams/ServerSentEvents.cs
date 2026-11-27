using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Veyra.Query.Streams;

/// <summary>
/// Writes a <c>text/event-stream</c> answer (ADR 0022): the headers that keep proxies from holding it back, named
/// events with JSON data, and comments that keep an idle connection open.
/// </summary>
internal static class ServerSentEvents
{
    /// <summary>How often a stream sends a comment, so proxies and browsers do not close it as idle.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>Sends the headers at once, so the browser knows the stream is open before the first event.</summary>
    /// <param name="context">The request.</param>
    /// <param name="cancellationToken">The request's abort token.</param>
    /// <returns>A task that completes when the headers are sent.</returns>
    public static async Task StartAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;

        // Every event must leave at once: nothing in Veyra may buffer the body, and nginx-style proxies are asked not
        // to either. Caddy streams text/event-stream by itself.
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";

        await response.StartAsync(cancellationToken);
        await response.WriteAsync(": open\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>Writes one event.</summary>
    /// <typeparam name="T">The data's type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="name">The event name, such as <c>rate</c>.</param>
    /// <param name="data">The data, sent as one line of JSON.</param>
    /// <param name="json">The API's JSON settings, so the data has the shape the REST API gives it.</param>
    /// <param name="cancellationToken">The request's abort token.</param>
    /// <param name="flush">Whether to send it now, or let a few more join it first.</param>
    /// <returns>A task that completes when the event is written.</returns>
    public static async Task WriteEventAsync<T>(
        HttpResponse response,
        string name,
        T data,
        JsonSerializerOptions json,
        CancellationToken cancellationToken,
        bool flush = true)
    {
        // The API's settings do not indent, so the data is one line, as a data field must be.
        var line = JsonSerializer.Serialize(data, json);

        await response.WriteAsync($"event: {name}\ndata: {line}\n\n", cancellationToken);

        if (flush)
        {
            await response.Body.FlushAsync(cancellationToken);
        }
    }

    /// <summary>Writes a comment, which browsers ignore and which keeps the connection busy.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">The request's abort token.</param>
    /// <returns>A task that completes when the comment is sent.</returns>
    public static async Task WriteHeartbeatAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        await response.WriteAsync(": heartbeat\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
