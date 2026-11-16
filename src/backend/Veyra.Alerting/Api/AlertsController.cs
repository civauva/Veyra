using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Alerts;
using Veyra.Alerting.Catalog;
using Veyra.Core.Alerts;
using Veyra.Core.Security;
using Veyra.Core.Time;

namespace Veyra.Alerting.Api;

/// <summary>
/// Serves the alerts: the history and its counts for the Alerts screen, and the details of one alert for the alert
/// screen and the links in notifications.
/// </summary>
[ApiController]
[Route("api/v1/alerts")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class AlertsController : ControllerBase
{
    private const int _defaultDays = 30;
    private const int _defaultLimit = 50;
    private const int _maxLimit = 200;

    private readonly AlertDetailsBuilder _details;
    private readonly AlertStore _store;
    private readonly IOptionsMonitor<AlertingOptions> _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Alerting module builds it, so its services can stay internal.</summary>
    /// <param name="details">Builds the details.</param>
    /// <param name="store">Lists the alerts.</param>
    /// <param name="options">The Alerting settings with the length of the history.</param>
    /// <param name="clock">The clock that decides what now is.</param>
    internal AlertsController(
        AlertDetailsBuilder details,
        AlertStore store,
        IOptionsMonitor<AlertingOptions> options,
        TimeProvider clock)
    {
        _details = details;
        _store = store;
        _options = options;
        _clock = clock;
    }

    /// <summary>
    /// Lists the alerts that were open at some time in the last days, newest first, a page at a time.
    /// </summary>
    /// <param name="days">How many days back, from 1 to the history kept (90 by default). Defaults to 30.</param>
    /// <param name="timeZone">The viewer's IANA time zone, whose midnight starts each day; UTC when not given.</param>
    /// <param name="state"><c>open</c>, <c>resolved</c>, or <c>all</c>, the default.</param>
    /// <param name="rule">Only alerts of these rules, for example <c>disk-filling</c>. Repeat for several.</param>
    /// <param name="severity">Only alerts whose worst severity was <c>Warning</c>, or <c>Caution</c>.</param>
    /// <param name="environment">Only alerts of this environment.</param>
    /// <param name="kind">Only alerts about a <c>service</c>, or a <c>host</c>.</param>
    /// <param name="q">Only alerts whose service, host, or disk holds this text.</param>
    /// <param name="limit">The most alerts on the page, 1 to 200. Defaults to 50.</param>
    /// <param name="cursor">The <c>next</c> cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancels the query when the client goes away.</param>
    /// <returns>The page.</returns>
    /// <response code="200">The page.</response>
    /// <response code="400">A filter, the limit, or the cursor is not valid.</response>
    [HttpGet]
    [ProducesResponseType<AlertListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] int? days,
        [FromQuery] string? timeZone,
        [FromQuery] string? state,
        [FromQuery] string[]? rule,
        [FromQuery] AlertSeverity? severity,
        [FromQuery] string? environment,
        [FromQuery] string? kind,
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        CancellationToken cancellationToken)
    {
        if (!ZoneOf(timeZone, out var zone))
        {
            return ZoneProblem();
        }

        if (Period(days, zone) is not { } from)
        {
            return DaysProblem();
        }

        bool? open;

        switch (state?.ToLowerInvariant())
        {
            case null or "" or "all":
                open = null;
                break;
            case "open":
                open = true;
                break;
            case "resolved":
                open = false;
                break;
            default:
                return Problem(
                    title: "The state must be open, resolved, or all.",
                    statusCode: StatusCodes.Status400BadRequest);
        }

        AlertBlockKind? blockKind = null;

        if (!string.IsNullOrEmpty(kind))
        {
            if (!AlertSubject.TryParseKind(kind, out var parsed))
            {
                return Problem(
                    title: "The kind must be service, host, database, or container.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            blockKind = parsed;
        }

        if (rule?.FirstOrDefault(name => !AlertRules.All.Contains(name)) is { } unknown)
        {
            return Problem(
                title: $"There is no rule named {unknown}. The rules are {string.Join(", ", AlertRules.All)}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var pageSize = limit ?? _defaultLimit;

        if (pageSize is < 1 or > _maxLimit)
        {
            return Problem(
                title: $"The limit must be between 1 and {_maxLimit}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        AlertCursor? after = null;

        if (cursor is not null && !AlertCursor.TryDecode(cursor, out after))
        {
            return Problem(title: "The cursor is not valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        var page = await _store.ListPageAsync(
            new AlertListQuery(
                From: from,
                To: _clock.GetUtcNow(),
                Limit: pageSize,
                Open: open,
                Rules: rule,
                Severity: severity,
                Environment: string.IsNullOrEmpty(environment) ? null : environment,
                Kind: blockKind,
                Block: q,
                After: after),
            cancellationToken);

        return Ok(new AlertListResponse(page.Items.Select(AlertListItemResponse.Of).ToList(), page.Next?.Encode()));
    }

    /// <summary>
    /// Counts the alerts of the last days: open now, in all, at warning, how long they lasted, per rule, and per day.
    /// </summary>
    /// <param name="days">How many days back, from 1 to the history kept (90 by default). Defaults to 30.</param>
    /// <param name="timeZone">The viewer's IANA time zone, whose midnight starts each day; UTC when not given.</param>
    /// <param name="environment">Only alerts of this environment.</param>
    /// <param name="cancellationToken">Cancels the query when the client goes away.</param>
    /// <returns>The counts.</returns>
    /// <response code="200">The counts.</response>
    /// <response code="400">The number of days is not valid.</response>
    [HttpGet("summary")]
    [ProducesResponseType<AlertSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SummaryAsync(
        [FromQuery] int? days,
        [FromQuery] string? timeZone,
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        if (!ZoneOf(timeZone, out var zone))
        {
            return ZoneProblem();
        }

        if (Period(days, zone) is not { } from)
        {
            return DaysProblem();
        }

        var now = _clock.GetUtcNow();
        var spans = await _store.ListSpansAsync(
            from,
            now,
            string.IsNullOrEmpty(environment) ? null : environment,
            cancellationToken);

        return Ok(AlertSummaries.Summarise(spans, from, now, _options.CurrentValue, zone));
    }

    /// <summary>
    /// Lists the catalog rules with the words their alerts use and their levels (ADR 0020), so screens word alerts of
    /// rules they were not built with.
    /// </summary>
    /// <returns>The rules, in the order screens list them.</returns>
    /// <response code="200">The rules.</response>
    [HttpGet("catalog")]
    [ProducesResponseType<IReadOnlyList<CatalogRuleResponse>>(StatusCodes.Status200OK)]
    public IReadOnlyList<CatalogRuleResponse> Catalog()
    {
        var settings = _options.CurrentValue;

        return RuleCatalog.All
            .Select(rule =>
            {
                var (caution, warning) = RuleCatalog.Levels(rule, settings);

                return new CatalogRuleResponse(
                    rule.Id,
                    rule.Name,
                    rule.Description,
                    rule.Unit.ToString().ToLowerInvariant(),
                    rule.Noun,
                    rule.Headline,
                    rule.Recovery,
                    rule.PartLabel?.TrimEnd(':'),
                    rule.LowerIsWorse,
                    RuleCatalog.Enabled(rule, settings),
                    caution,
                    warning);
            })
            .ToList();
    }

    /// <summary>Returns one alert, open or resolved within the history kept (90 days by default).</summary>
    /// <param name="id">The alert's ID.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The details.</returns>
    /// <response code="200">The details.</response>
    /// <response code="404">No alert has this ID, or it resolved longer ago than the history is kept.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AlertDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await _details.BuildAsync(id, cancellationToken) is { } details ? Ok(details) : NotFound();

    // The start of the period: midnight in the viewer's zone, the given number of days back, today counting as one.
    private DateTimeOffset? Period(int? days, TimeZoneInfo zone)
    {
        var count = days ?? _defaultDays;

        if (count < 1 || count > _options.CurrentValue.HistoryDays)
        {
            return null;
        }

        return TimeZones.LocalMidnight(_clock.GetUtcNow(), zone, count - 1);
    }

    // No zone means UTC, as before VEY-22; a name that is not an IANA zone is refused.
    private static bool ZoneOf(string? name, out TimeZoneInfo zone)
    {
        if (string.IsNullOrEmpty(name))
        {
            zone = TimeZoneInfo.Utc;

            return true;
        }

        var known = TimeZones.TryFind(name, out var found);
        zone = found ?? TimeZoneInfo.Utc;

        return known;
    }

    private ObjectResult ZoneProblem() => Problem(
        title: "The time zone must be an IANA name, such as Europe/Berlin.",
        statusCode: StatusCodes.Status400BadRequest);

    private ObjectResult DaysProblem() => Problem(
        title: $"The days must be between 1 and {_options.CurrentValue.HistoryDays}.",
        statusCode: StatusCodes.Status400BadRequest);
}
