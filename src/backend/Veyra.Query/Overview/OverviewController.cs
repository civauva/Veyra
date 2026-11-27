using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Metrics;
using Veyra.Query.Overview.Api;

namespace Veyra.Query.Overview;

/// <summary>
/// The Console look's Overview (ADR 0023): the whole environment in one read, so every figure comes from the same
/// moment and a refresh is one request. Every figure, order, and filter is worked out on the server.
/// </summary>
[ApiController]
[Route("api/v1/overview")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class OverviewController : ControllerBase
{
    private readonly OverviewBuilder _builder;

    private readonly IAuthorizationService _authorization;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Query module builds it, so its builder can stay internal.</summary>
    /// <param name="builder">Builds the Overview.</param>
    /// <param name="authorization">Decides whether the person is an Admin, who alone sees the agents.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal OverviewController(OverviewBuilder builder, IAuthorizationService authorization, TimeProvider clock)
    {
        _builder = builder;
        _authorization = authorization;
        _clock = clock;
    }

    /// <summary>The Overview of one environment, or of all of them, over one window.</summary>
    /// <param name="environment">The environment; all when omitted.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c> (the default), <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="databases">
    /// The most databases to list, from 1 to 50: as many as fit in the screen's tile. 12 when omitted.
    /// </param>
    /// <param name="pipelines">
    /// The most pipelines to list, from 1 to 50: as many as fit in the screen's tile. 8 when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The Overview.</returns>
    /// <response code="200">The Overview; its collectors are null for anyone but an Admin.</response>
    /// <response code="400">The window is not positive or longer than 31 days, or a number of rows is out of range.</response>
    [HttpGet]
    [ProducesResponseType<OverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string? environment,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? databases,
        [FromQuery] int? pipelines,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        if (databases is < 1 or > OverviewRows.Most || pipelines is < 1 or > OverviewRows.Most)
        {
            return Problem(
                title: $"The number of rows must be between 1 and {OverviewRows.Most}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rows = new OverviewRows(
            databases ?? OverviewRows.Default.Databases,
            pipelines ?? OverviewRows.Default.Pipelines);

        var admin = (await _authorization.AuthorizeAsync(User, VeyraPolicies.Admin)).Succeeded;

        return Ok(await _builder.BuildAsync(environment, span, admin, rows, cancellationToken));
    }
}
