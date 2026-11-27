using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Metrics;
using Veyra.Core.Security;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Serves the Metrics screens: the overview, one host, one service, and the metric browser (ADR 0010). Every action
/// takes a <c>range</c> ending now (<c>15m</c>, <c>1h</c>, <c>6h</c> by default, <c>24h</c>, <c>7d</c>, or
/// <c>30d</c>), or <c>from</c> and <c>to</c> in ISO 8601 UTC, up to 31 days apart (ADR 0022).
/// </summary>
[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class MetricsController : ControllerBase
{
    private readonly MetricViewBuilder _views;

    private readonly DatabaseViewBuilder _databases;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Query module builds it, so its builders can stay internal.</summary>
    /// <param name="views">Builds the views.</param>
    /// <param name="databases">Builds the database servers' screens.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal MetricsController(MetricViewBuilder views, DatabaseViewBuilder databases, TimeProvider clock)
    {
        _views = views;
        _databases = databases;
        _clock = clock;
    }

    /// <summary>
    /// Returns the hosts, services, database servers, and containers that sent metrics in the last 15 minutes and match
    /// the search and the environment, with the figures over them.
    /// </summary>
    /// <param name="range">The window of the lines, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="search">Text a card's name must hold, in any case (VEY-67); every card when omitted.</param>
    /// <param name="environment">
    /// The environment; hosts, database servers, and containers that name none belong to every one. All when omitted.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The overview.</returns>
    /// <response code="200">The overview.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    [HttpGet("overview")]
    [ProducesResponseType<MetricsOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OverviewAsync(
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? search,
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return Ok(await _views.OverviewAsync(span, new MetricsOverviewFilter(search, environment), cancellationToken));
    }

    /// <summary>Returns one host's CPU, memory, disks, network, load, and its Docker containers.</summary>
    /// <param name="host">The host name, from <c>host.name</c>.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The host's metrics.</returns>
    /// <response code="200">The host's metrics.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">The host sent no host metrics within the window.</response>
    [HttpGet("hosts/{host}")]
    [ProducesResponseType<HostMetricsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HostAsync(
        string host,
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

        return await _views.HostAsync(host, span, cancellationToken) is { } view ? Ok(view) : NotFound();
    }

    /// <summary>
    /// Returns one database server's requests, blocking, deadlocks, waits, databases, SQL Agent jobs, and the queries
    /// running now (ADR 0018).
    /// </summary>
    /// <param name="engine">The engine, from <c>db.system.name</c>, such as <c>microsoft.sql_server</c>.</param>
    /// <param name="server">The server, from <c>server.address</c>.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="system">
    /// Whether the server's own databases, jobs, statements, and tables are listed (VEY-64); left out when omitted, and
    /// counted in the <c>hidden</c> fields instead.
    /// </param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The server's metrics.</returns>
    /// <response code="200">The server's metrics.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">The server sent no metrics within the window.</response>
    [HttpGet("databases/{engine}/{server}")]
    [ProducesResponseType<DatabaseMetricsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DatabaseAsync(
        string engine,
        string server,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] bool system,
        CancellationToken cancellationToken)
    {
        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        return await _databases.DatabaseAsync(engine, server, span, system, cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }

    /// <summary>Returns one service's request durations, routes, and process and runtime metrics.</summary>
    /// <param name="service">The service name, from <c>service.name</c>.</param>
    /// <param name="environment">The environment; leave it out for a service that names none.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The service's metrics.</returns>
    /// <response code="200">The service's metrics.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">The service sent none of these metrics within the window.</response>
    [HttpGet("services/{service}")]
    [ProducesResponseType<ServiceMetricsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServiceAsync(
        string service,
        [FromQuery] string? environment,
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

        return await _views.ServiceAsync(
            service,
            string.IsNullOrEmpty(environment) ? null : environment,
            span,
            cancellationToken) is { } view
            ? Ok(view)
            : NotFound();
    }

    /// <summary>Lists the metric names seen within the window, for the browser.</summary>
    /// <param name="search">Only names that contain this text, ignoring case.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the read when the client goes away.</param>
    /// <returns>The names, alphabetically.</returns>
    /// <response code="200">The names.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    [HttpGet("names")]
    [ProducesResponseType<IReadOnlyList<MetricName>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> NamesAsync(
        [FromQuery] string? search,
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

        return Ok(await _views.NamesAsync(search, span, cancellationToken));
    }

    /// <summary>Charts one metric for the browser, as one line or split by an attribute.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="show">
    /// <c>value</c>, <c>rate</c>, <c>p50</c>, <c>p95</c>, or <c>p99</c>; left out, what suits the metric.
    /// </param>
    /// <param name="split">An attribute key to split by, such as <c>http.route</c> or <c>service.name</c>.</param>
    /// <param name="range">The window, ending now.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="service">Only this service's series.</param>
    /// <param name="host">Only this host's series.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The chart.</returns>
    /// <response code="200">The chart.</response>
    /// <response code="400">No name was given, or the window is not positive or longer than 31 days.</response>
    /// <response code="404">No series has the name within the window.</response>
    [HttpGet("chart")]
    [ProducesResponseType<MetricChartResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChartAsync(
        [FromQuery] string? name,
        [FromQuery] string? show,
        [FromQuery] string? split,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? service,
        [FromQuery] string? host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Problem(title: "Give the metric name.", statusCode: StatusCodes.Status400BadRequest);
        }

        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        var chart = await _views.ChartAsync(
            name,
            show,
            string.IsNullOrWhiteSpace(split) ? null : split,
            span,
            service,
            host,
            cancellationToken);

        return chart is null ? NotFound() : Ok(chart);
    }
}
