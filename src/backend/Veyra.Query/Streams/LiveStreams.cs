using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Veyra.Query.Streams;

/// <summary>What the two live streams share: refusing one too many, and telling a gone browser from a fault.</summary>
internal static class LiveStreams
{
    /// <summary>The problem answer when the instance serves as many live streams as allowed.</summary>
    /// <param name="controller">The controller that answers.</param>
    /// <returns><c>503</c> with a problem body.</returns>
    public static ObjectResult Full(ControllerBase controller) => controller.Problem(
        title: "Too many live streams are open on this Veyra instance.",
        detail: "Close Live in another browser or tab, or raise Veyra:Query:LiveListeners on the Settings screen.",
        statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>Whether a failure while streaming only means that the browser went away.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="cancellationToken">The request's abort token.</param>
    /// <returns><see langword="true"/> for a disconnect, which ends the stream quietly.</returns>
    public static bool IsDisconnect(Exception exception, CancellationToken cancellationToken) =>
        exception is IOException
        || (exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
}
