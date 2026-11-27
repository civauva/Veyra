using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Security;
using Veyra.Query.Search.Api;

namespace Veyra.Query.Search;

/// <summary>
/// The search in the top bar (ADR 0022): services, hosts and containers, databases, root operations, and how many log
/// records hold the words, in one answer.
/// </summary>
[ApiController]
[Route("api/v1/search")]
[Authorize(Policy = VeyraPolicies.Viewer)]
public sealed class SearchController : ControllerBase
{
    // As for logs and traces: a wider window is refused.
    private static readonly TimeSpan _maxWindow = TimeSpan.FromDays(31);

    private static readonly TimeSpan _defaultWindow = TimeSpan.FromHours(24);

    // Longer text is no name anyone types, and would only make every group slower.
    private const int _maxText = 200;

    private readonly SearchBuilder _builder;
    private readonly TimeProvider _clock;

    /// <summary>Creates the controller. The module builds it, because the builder is its own.</summary>
    /// <param name="builder">Finds and ranks what the search asks for.</param>
    /// <param name="clock">The clock for the default window.</param>
    internal SearchController(SearchBuilder builder, TimeProvider clock)
    {
        _builder = builder;
        _clock = clock;
    }

    /// <summary>Searches every signal for the text.</summary>
    /// <remarks>
    /// Each group holds up to five items, best first: exact names, then names that start with the text, then names
    /// that hold it. Each group has 150 ms; one that runs out is marked <c>partial</c> with what it found by then. An
    /// empty <paramref name="q"/> answers every group empty.
    /// </remarks>
    /// <param name="q">The text to look for, in any case.</param>
    /// <param name="environment">Only this environment; all by default.</param>
    /// <param name="from">Start of the window, inclusive, in ISO 8601. Defaults to 24 hours before the end.</param>
    /// <param name="to">End of the window, exclusive, in ISO 8601. Defaults to now.</param>
    /// <param name="cancellationToken">Cancels the search when the client goes away.</param>
    /// <returns>The groups.</returns>
    /// <response code="200">The groups, possibly empty.</response>
    /// <response code="400">The window is not positive or longer than 31 days, or the text is too long.</response>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync(
        [FromQuery] string? q,
        [FromQuery] string? environment,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var end = to ?? _clock.GetUtcNow();
        var start = from ?? end - _defaultWindow;

        if (start >= end || end - start > _maxWindow)
        {
            return Problem(
                title: $"The time window must be positive and at most {_maxWindow.TotalDays} days.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var text = q?.Trim() ?? string.Empty;

        if (text.Length > _maxText)
        {
            return Problem(
                title: $"The search text may be at most {_maxText} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var found = await _builder.SearchAsync(
            text,
            string.IsNullOrWhiteSpace(environment) ? null : environment.Trim(),
            start,
            end,
            cancellationToken);

        return Ok(found);
    }
}
