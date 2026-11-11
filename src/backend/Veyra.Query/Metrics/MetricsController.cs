using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Metrics;
using Veyra.Core.Security;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Serves the Metrics screens: the overview, one host, one service, and the metric browser (ADR 0010). Every action
/// takes a <c>range</c> of <c>1h</c>, <c>6h</c> (the default), <c>24h</c>, or <c>7d</c>.
/// </summary>
[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class MetricsController : ControllerBase
{
    private readonly MetricViewBuilder _views;

    /// <summary>Creates the controller. The Query module builds it, so its builder can stay internal.</summary>
    /// <param name="views">Builds the views.</param>
    internal MetricsController(MetricViewBuilder views)
    {
        _views = views;
    }

    /// <summary>Returns every host and service that sent metrics in the last 15 minutes.</summary>
    /// <param name="range">The window of the lines.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The overview.</returns>
    [HttpGet("overview")]
    public Task<MetricsOverviewResponse> OverviewAsync(
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        _views.OverviewAsync(range, cancellationToken);

    /// <summary>Returns one host's CPU, memory, disks, network, and load.</summary>
    /// <param name="host">The host name, from <c>host.name</c>.</param>
    /// <param name="range">The window.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The host's metrics.</returns>
    /// <response code="200">The host's metrics.</response>
    /// <response code="404">The host sent no host metrics within the window.</response>
    [HttpGet("hosts/{host}")]
    [ProducesResponseType<HostMetricsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HostAsync(
        string host,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        await _views.HostAsync(host, range, cancellationToken) is { } view ? Ok(view) : NotFound();

    /// <summary>Returns one service's request durations, routes, and process and runtime metrics.</summary>
    /// <param name="service">The service name, from <c>service.name</c>.</param>
    /// <param name="environment">The environment; leave it out for a service that names none.</param>
    /// <param name="range">The window.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The service's metrics.</returns>
    /// <response code="200">The service's metrics.</response>
    /// <response code="404">The service sent none of these metrics within the window.</response>
    [HttpGet("services/{service}")]
    [ProducesResponseType<ServiceMetricsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ServiceAsync(
        string service,
        [FromQuery] string? environment,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        await _views.ServiceAsync(
            service,
            string.IsNullOrEmpty(environment) ? null : environment,
            range,
            cancellationToken) is { } view
            ? Ok(view)
            : NotFound();

    /// <summary>Lists the metric names seen within the window, for the browser.</summary>
    /// <param name="search">Only names that contain this text, ignoring case.</param>
    /// <param name="range">The window.</param>
    /// <param name="cancellationToken">Cancels the read when the client goes away.</param>
    /// <returns>The names, alphabetically.</returns>
    [HttpGet("names")]
    public Task<IReadOnlyList<MetricName>> NamesAsync(
        [FromQuery] string? search,
        [FromQuery] string? range,
        CancellationToken cancellationToken) =>
        _views.NamesAsync(search, range, cancellationToken);

    /// <summary>Charts one metric for the browser, as one line or split by an attribute.</summary>
    /// <param name="name">The metric name.</param>
    /// <param name="show">
    /// <c>value</c>, <c>rate</c>, <c>p50</c>, <c>p95</c>, or <c>p99</c>; left out, what suits the metric.
    /// </param>
    /// <param name="split">An attribute key to split by, such as <c>http.route</c> or <c>service.name</c>.</param>
    /// <param name="range">The window.</param>
    /// <param name="service">Only this service's series.</param>
    /// <param name="host">Only this host's series.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The chart.</returns>
    /// <response code="200">The chart.</response>
    /// <response code="400">No name was given.</response>
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
        [FromQuery] string? service,
        [FromQuery] string? host,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Problem(title: "Give the metric name.", statusCode: StatusCodes.Status400BadRequest);
        }

        var chart = await _views.ChartAsync(
            name,
            show,
            string.IsNullOrWhiteSpace(split) ? null : split,
            range,
            service,
            host,
            cancellationToken);

        return chart is null ? NotFound() : Ok(chart);
    }
}
