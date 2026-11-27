using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Veyra.Query.Metrics;

/// <summary>
/// Checks the period a request asks for in one place, so every endpoint that takes <c>range</c>, <c>from</c>, and
/// <c>to</c> refuses the same periods with the same problem as the log search (ADR 0022).
/// </summary>
internal static class MetricSpans
{
    /// <summary>Refuses a period that is not positive or longer than 31 days.</summary>
    /// <param name="controller">The controller answering the request, which builds the problem.</param>
    /// <param name="span">The period asked for.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    /// <returns>A 400 problem, or null when the period may be read.</returns>
    public static ObjectResult? Refuse(ControllerBase controller, MetricSpan span, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(clock);

        return span.Resolve(clock.GetUtcNow()) is null
            ? controller.Problem(title: MetricSpan.Refusal, statusCode: StatusCodes.Status400BadRequest)
            : null;
    }
}
