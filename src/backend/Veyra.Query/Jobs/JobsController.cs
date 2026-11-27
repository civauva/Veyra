using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Jobs.Api;
using Veyra.Query.Metrics;

namespace Veyra.Query.Jobs;

/// <summary>
/// Jobs and pipelines (ADR 0027): every job Veyra knows from traces and from SQL Server Agent, and one job's runs.
/// Every filter, order, and figure is worked out on the server.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class JobsController : ControllerBase
{
    private readonly JobsBuilder _builder;

    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The Query module builds it, so its builder can stay internal.</summary>
    /// <param name="builder">Builds the list and a job's screen.</param>
    /// <param name="clock">The clock that decides what now is, for a window that ends now.</param>
    internal JobsController(JobsBuilder builder, TimeProvider clock)
    {
        _builder = builder;
        _clock = clock;
    }

    /// <summary>Every job with runs in the window or a run going on, those whose latest run failed first.</summary>
    /// <param name="environment">The environment; all when omitted.</param>
    /// <param name="range">
    /// The window, ending now: <c>15m</c>, <c>1h</c>, <c>6h</c> (the default), <c>24h</c>, <c>7d</c>, or <c>30d</c>.
    /// </param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="source"><c>trace</c> or <c>sqlagent</c>; both when omitted.</param>
    /// <param name="q">Only jobs whose name or block contains this text, in any case.</param>
    /// <param name="status"><c>failing</c>, <c>running</c>, or <c>all</c> (the default).</param>
    /// <param name="system">Whether the jobs SQL Server set up for itself are listed; left out when omitted.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The list.</returns>
    /// <response code="200">The list.</response>
    /// <response code="400">The window, the source, or the status is not one the API knows.</response>
    [HttpGet]
    [ProducesResponseType<JobListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] string? environment,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? source,
        [FromQuery] string? q,
        [FromQuery] string? status,
        [FromQuery] bool system,
        CancellationToken cancellationToken)
    {
        if (new MetricSpan(range, from, to).Resolve(_clock.GetUtcNow()) is not { } period)
        {
            return Problem(title: MetricSpan.Refusal, statusCode: StatusCodes.Status400BadRequest);
        }

        var chosenSource = JobText.SourceOf(source);

        if (!string.IsNullOrWhiteSpace(source) && chosenSource is null)
        {
            return Problem(
                title: "The source must be trace or sqlagent.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (StatusOf(status) is not { } chosenStatus)
        {
            return Problem(
                title: "The status must be failing, running, or all.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Ok(await _builder.ListAsync(
            period,
            environment,
            chosenSource,
            q,
            chosenStatus,
            system,
            cancellationToken));
    }

    /// <summary>One job: its figures, durations, and runs of the window, newest first, with their steps.</summary>
    /// <param name="id">The job's ID, as the list gave it.</param>
    /// <param name="range">The window, ending now; <c>6h</c> by default.</param>
    /// <param name="from">The start of the window, in ISO 8601 UTC, instead of a range.</param>
    /// <param name="to">The end of the window, in ISO 8601 UTC; now when omitted.</param>
    /// <param name="cancellationToken">Cancels the reads when the client goes away.</param>
    /// <returns>The job's screen.</returns>
    /// <response code="200">The job.</response>
    /// <response code="400">The window is not positive or longer than 31 days.</response>
    /// <response code="404">No job has this ID, or Veyra keeps no run of it.</response>
    [HttpGet("{id}")]
    [ProducesResponseType<JobDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        string id,
        [FromQuery] string? range,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (new MetricSpan(range, from, to).Resolve(_clock.GetUtcNow()) is not { } period)
        {
            return Problem(title: MetricSpan.Refusal, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!JobIds.TryDecode(id, out var job)
            || await _builder.GetAsync(job, period, cancellationToken) is not { } detail)
        {
            return Problem(title: "There is no such job.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(detail);
    }

    private static JobStatusFilter? StatusOf(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        null or "" or "all" => JobStatusFilter.All,
        "failing" => JobStatusFilter.Failing,
        "running" => JobStatusFilter.Running,
        _ => null,
    };
}
