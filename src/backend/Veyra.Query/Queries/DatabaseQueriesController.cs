using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Metrics;
using Veyra.Query.Queries.Api;

namespace Veyra.Query.Queries;

/// <summary>A database server's statements and their plans (ADR 0020, area 4).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseQueriesController : ControllerBase
{
    private readonly QueriesViewBuilder _queries;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller.</summary>
    /// <param name="queries">Builds the Queries tab and statement pages.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal DatabaseQueriesController(QueriesViewBuilder queries, TimeProvider clock)
    {
        _queries = queries;
        _clock = clock;
    }

    /// <summary>The statements over the range, ranked on the server by the measure asked for (VEY-66).</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, such as <c>6h</c>, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range (ADR 0022).</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="system">Whether the server's own statements are listed; left out when omitted.</param>
    /// <param name="sort">
    /// What the statements are ranked by: <c>totalTime</c> (the default), <c>cpu</c>, <c>reads</c>, <c>runs</c>, or
    /// <c>timePerRun</c>.
    /// </param>
    /// <param name="simple">
    /// Whether only the statements with a finding are listed, as the Simple view shows them (VEY-67), with the busiest
    /// statement named apart.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Queries tab.</returns>
    /// <response code="200">The Queries tab.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    [HttpGet("queries")]
    [ProducesResponseType<QueriesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueriesAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool system,
        [FromQuery] QuerySort? sort,
        [FromQuery] bool simple,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return Ok(await _queries.ListAsync(
            engine,
            server,
            span,
            new QueryListing(system, sort ?? QuerySort.TotalTime, simple),
            cancellationToken));
    }

    /// <summary>One statement's page.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="queryHash">The statement's query hash, such as <c>0x6ACBDEE9D5014909</c>.</param>
    /// <param name="range">The range, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range (ADR 0022).</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page; 404 when the statement did not run in the range.</returns>
    [HttpGet("queries/{queryHash}")]
    [ProducesResponseType<QueryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryAsync(
        string engine,
        string server,
        string queryHash,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return await _queries.QueryAsync(engine, server, queryHash, span, cancellationToken) is { } page
            ? Ok(page)
            : NotFound();
    }

    /// <summary>A captured plan as a <c>.sqlplan</c> file for SQL Server Management Studio.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="planHash">The plan hash.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file; 404 when Veyra holds no such plan.</returns>
    [HttpGet("plans/{planHash}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlanAsync(
        string engine,
        string server,
        string planHash,
        CancellationToken cancellationToken) =>
        await _queries.PlanXmlAsync(engine, server, planHash, cancellationToken) is { } xml
            ? File(Encoding.UTF8.GetBytes(xml), "application/xml", $"{planHash}.sqlplan")
            : NotFound();
}
