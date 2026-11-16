using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Queries.Api;

namespace Veyra.Query.Queries;

/// <summary>A database server's statements and their plans (ADR 0020, area 4).</summary>
[ApiController]
[Route("api/v1/metrics/databases/{engine}/{server}")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class DatabaseQueriesController : ControllerBase
{
    private readonly QueriesViewBuilder _queries;

    /// <summary>Creates the controller.</summary>
    /// <param name="queries">Builds the Queries tab and statement pages.</param>
    internal DatabaseQueriesController(QueriesViewBuilder queries)
    {
        _queries = queries;
    }

    /// <summary>The statements over the range, by total time.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="range">The range, such as <c>6h</c>.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The Queries tab.</returns>
    [HttpGet("queries")]
    public Task<QueriesResponse> QueriesAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        _queries.ListAsync(engine, server, range, cancellationToken);

    /// <summary>One statement's page.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="queryHash">The statement's query hash, such as <c>0x6ACBDEE9D5014909</c>.</param>
    /// <param name="range">The range.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page; 404 when the statement did not run in the range.</returns>
    [HttpGet("queries/{queryHash}")]
    [ProducesResponseType<QueryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QueryAsync(
        string engine,
        string server,
        string queryHash,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        await _queries.QueryAsync(engine, server, queryHash, range, cancellationToken) is { } page
            ? Ok(page)
            : NotFound();

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
