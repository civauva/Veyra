using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Logs;

/// <summary>
/// Builds the storage query of a logs request from its parameters and its <c>q</c>, the same way for every logs
/// endpoint (ADR 0024).
/// </summary>
internal static class LogRequests
{
    /// <summary>Checks the filters and builds the query. Page size, cursor, and order are set by the caller.</summary>
    /// <param name="filters">The filters as the query string gave them.</param>
    /// <param name="from">The start of the window.</param>
    /// <param name="to">The end of the window.</param>
    /// <param name="query">The query, when the filters are good.</param>
    /// <returns>The problem with the filters; <see langword="null"/> when they are good.</returns>
    public static LogFilterProblem? TryBuild(
        LogFilterParameters filters,
        DateTimeOffset from,
        DateTimeOffset to,
        out LogQuery? query)
    {
        ArgumentNullException.ThrowIfNull(filters);

        query = null;

        if (!TryParseAttributes(filters.Attribute, out var attributes))
        {
            return new LogFilterProblem("Each attribute filter must be written key:value, with a non-empty key.");
        }

        var parse = LogQueryParser.Parse(filters.Q);

        if (parse.Error is { } error)
        {
            return new LogFilterProblem(error.Message, error.Position, error.Expected);
        }

        var given = new LogQuery(
            from,
            to,
            filters.Severity,
            filters.Service,
            filters.Text,
            filters.TraceId,
            1,
            null,
            filters.Environment,
            attributes,
            string.IsNullOrWhiteSpace(filters.SpanId) ? null : filters.SpanId.Trim(),
            Hosts: filters.Host is { Length: > 0 } hosts ? hosts : null,
            Levels: filters.Level is { Length: > 0 } levels ? levels : null);

        query = LogQueryFilters.Apply(given, parse.Terms);

        return null;
    }

    /// <summary>
    /// Answers a problem with the filters as <c>400</c>, with the place and the expectation of a query that does not
    /// parse as the members <c>position</c> and <c>expected</c>, so the bar can underline it.
    /// </summary>
    /// <param name="controller">The controller that answers.</param>
    /// <param name="problem">The problem.</param>
    /// <returns>The problem response.</returns>
    public static ObjectResult Problem(ControllerBase controller, LogFilterProblem problem)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(problem);

        var details = controller.ProblemDetailsFactory.CreateProblemDetails(
            controller.HttpContext,
            StatusCodes.Status400BadRequest,
            problem.Title);

        if (problem.Position is { } position)
        {
            details.Extensions["position"] = position;
            details.Extensions["expected"] = problem.Expected;
        }

        return new ObjectResult(details) { StatusCode = StatusCodes.Status400BadRequest };
    }

    /// <summary>
    /// Reads attribute filters written <c>key:value</c>. Splits at the first colon, so values may contain colons, as
    /// URLs and times do.
    /// </summary>
    /// <param name="filters">The filters as the query string gave them.</param>
    /// <param name="attributes">The pairs, or <see langword="null"/> without filters.</param>
    /// <returns><see langword="false"/> when a filter has no colon or an empty key.</returns>
    internal static bool TryParseAttributes(string[]? filters, out Dictionary<string, string>? attributes)
    {
        attributes = null;

        if (filters is not { Length: > 0 })
        {
            return true;
        }

        attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var filter in filters)
        {
            var separator = filter.IndexOf(':', StringComparison.Ordinal);

            if (separator < 1)
            {
                return false;
            }

            attributes[filter[..separator]] = filter[(separator + 1)..];
        }

        return true;
    }
}
