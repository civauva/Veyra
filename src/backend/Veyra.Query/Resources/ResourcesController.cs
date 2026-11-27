using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Core.Security;
using Veyra.Query.Metrics;
using Veyra.Query.Resources.Api;

namespace Veyra.Query.Resources;

/// <summary>
/// The Resources lists of the Console look (ADR 0022): every service, host and container, and database server Veyra
/// knows, a page at a time, worst first. Every filter, count, sort, and comparison is worked out on the server.
/// </summary>
[ApiController]
[Route("api/v1/resources")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class ResourcesController : ControllerBase
{
    private const int _defaultLimit = 50;

    private readonly ServiceListBuilder _services;

    private readonly HostListBuilder _hosts;

    private readonly DatabaseListBuilder _databases;

    private readonly IOptionsMonitor<QueryOptions> _options;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Query module builds it, so its builders can stay internal.</summary>
    /// <param name="services">Builds the Services list.</param>
    /// <param name="hosts">Builds the Hosts and containers list.</param>
    /// <param name="databases">Builds the Databases list.</param>
    /// <param name="options">The Query settings, for the largest page.</param>
    /// <param name="clock">The clock that decides what now is, for a period that ends now.</param>
    internal ResourcesController(
        ServiceListBuilder services,
        HostListBuilder hosts,
        DatabaseListBuilder databases,
        IOptionsMonitor<QueryOptions> options,
        TimeProvider clock)
    {
        _services = services;
        _hosts = hosts;
        _databases = databases;
        _options = options;
        _clock = clock;
    }

    /// <summary>One page of the services, with the header strip over all of them.</summary>
    /// <param name="environment">The environment; services must name it. All when omitted.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c>, <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="health">
    /// <c>ok</c>, <c>caution</c>, <c>warning</c>, <c>unknown</c>, or <c>silent</c>; repeat it or separate with commas
    /// for several. Every service when omitted.
    /// </param>
    /// <param name="host">Only services that run on this host, from <c>host.name</c> on their resources.</param>
    /// <param name="search">Text the name must hold, in any case.</param>
    /// <param name="sort"><c>worst</c> first (the default) or <c>name</c>.</param>
    /// <param name="cursor">The <c>next</c> of the previous page, with the same filters.</param>
    /// <param name="limit">The most services on the page, 1 to <c>Veyra:Query:MaxPageSize</c>; 50 by default.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page, possibly empty.</response>
    /// <response code="400">A parameter is not one the list knows, or the cursor is not one Veyra issued.</response>
    [HttpGet("services")]
    [ProducesResponseType<ServiceListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ServicesAsync(
        [FromQuery] string? environment,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string[]? health,
        [FromQuery] string? host,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (Read(environment, range, from, to, health, search, sort, cursor, limit, out var query) is { } refused)
        {
            return refused;
        }

        return await _services.ListAsync(query!, host, cancellationToken) is { } list ? Ok(list) : BadCursor();
    }

    /// <summary>One page of the hosts and containers, with the header strip over all of them.</summary>
    /// <param name="environment">The environment; hosts and containers that name none belong to every one.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c>, <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="kind"><c>host</c> or <c>container</c>; both when omitted.</param>
    /// <param name="health">The health chips, as for the services.</param>
    /// <param name="state"><c>online</c> or <c>silent</c>; both when omitted.</param>
    /// <param name="search">Text the name must hold, in any case.</param>
    /// <param name="sort"><c>worst</c> first (the default) or <c>name</c>.</param>
    /// <param name="cursor">The <c>next</c> of the previous page, with the same filters.</param>
    /// <param name="limit">The most items on the page, 1 to <c>Veyra:Query:MaxPageSize</c>; 50 by default.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page, possibly empty.</response>
    /// <response code="400">A parameter is not one the list knows, or the cursor is not one Veyra issued.</response>
    [HttpGet("hosts")]
    [ProducesResponseType<HostListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HostsAsync(
        [FromQuery] string? environment,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? kind,
        [FromQuery] string[]? health,
        [FromQuery] string? state,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (Read(environment, range, from, to, health, search, sort, cursor, limit, out var query) is { } refused)
        {
            return refused;
        }

        if (!OneOf(kind, HostListBuilder.HostKind, HostListBuilder.ContainerKind))
        {
            return Problem(title: "The kind must be host or container.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!OneOf(state, HostListBuilder.Online, HostListBuilder.Silent))
        {
            return Problem(title: "The state must be online or silent.", statusCode: StatusCodes.Status400BadRequest);
        }

        var list = await _hosts.ListAsync(
            query!,
            string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
            string.IsNullOrWhiteSpace(state) ? null : state.Trim(),
            cancellationToken);

        return list is null ? BadCursor() : Ok(list);
    }

    /// <summary>One page of the database servers, with the header strip over all of them.</summary>
    /// <param name="environment">The environment; servers that name none belong to every one.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c>, <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="engine">Only this engine, such as <c>microsoft.sql_server</c> or <c>postgresql</c>.</param>
    /// <param name="health">The health chips, as for the services.</param>
    /// <param name="search">Text the server's name must hold, in any case.</param>
    /// <param name="sort"><c>worst</c> first (the default) or <c>name</c>.</param>
    /// <param name="system">Whether the servers' own databases, such as master, are counted; not by default.</param>
    /// <param name="cursor">The <c>next</c> of the previous page, with the same filters.</param>
    /// <param name="limit">The most servers on the page, 1 to <c>Veyra:Query:MaxPageSize</c>; 50 by default.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page, possibly empty.</response>
    /// <response code="400">A parameter is not one the list knows, or the cursor is not one Veyra issued.</response>
    [HttpGet("databases")]
    [ProducesResponseType<DatabaseListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DatabasesAsync(
        [FromQuery] string? environment,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? engine,
        [FromQuery] string[]? health,
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] bool system,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (Read(environment, range, from, to, health, search, sort, cursor, limit, out var query) is { } refused)
        {
            return refused;
        }

        return await _databases.ListAsync(query!, engine, system, cancellationToken) is { } list
            ? Ok(list)
            : BadCursor();
    }

    private static bool OneOf(string? value, params string[] allowed) =>
        string.IsNullOrWhiteSpace(value)
        || allowed.Any(one => string.Equals(one, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private ObjectResult BadCursor() =>
        Problem(title: "The cursor is not valid.", statusCode: StatusCodes.Status400BadRequest);

    // Checks what every list takes and builds the query; a problem when a part is not one the lists know.
    private ObjectResult? Read(
        string? environment,
        string? range,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string[]? health,
        string? search,
        string? sort,
        string? cursor,
        int? limit,
        out ResourceListQuery? query)
    {
        query = null;

        var span = new MetricSpan(range, from, to);

        if (MetricSpans.Refuse(this, span, _clock) is { } refused)
        {
            return refused;
        }

        var size = limit ?? _defaultLimit;
        var maxPageSize = _options.CurrentValue.MaxPageSize;

        if (size < 1 || size > maxPageSize)
        {
            return Problem(
                title: $"The limit must be between 1 and {maxPageSize}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!ResourceRanking.IsSort(sort))
        {
            return Problem(title: "The sort must be worst or name.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (ResourceHealthFilter.Parse(health) is not { } chips)
        {
            return Problem(
                title: "The health must be ok, caution, warning, unknown, or silent.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        query = new ResourceListQuery(environment, span, chips, search, sort, cursor, size);

        return null;
    }
}
