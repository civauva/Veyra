using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Veyra.Access.Sessions;
using Veyra.Core.Maintenance;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Clears all telemetry: every log, metric, trace, SQL Server activity record, and alert (VEY-59). Users, settings,
/// agents, monitored servers, ingestion keys, and notification channels stay, and monitoring carries on. Admins only;
/// the Admin types the site's host name, so a stray click or a forged request from another tab cannot clear.
/// </summary>
[ApiController]
[Route("api/v1/settings/clear-telemetry")]
[Authorize(Policy = VeyraPolicies.Admin)]
public sealed partial class ClearTelemetryController : ControllerBase
{
    private readonly IReadOnlyList<ITelemetryClearer> _clearers;

    private readonly ILogger<ClearTelemetryController> _logger;

    /// <summary>Creates the controller.</summary>
    /// <param name="clearers">Every module's clearer.</param>
    /// <param name="logger">Records who cleared and when.</param>
    internal ClearTelemetryController(
        IEnumerable<ITelemetryClearer> clearers,
        ILogger<ClearTelemetryController> logger)
    {
        _clearers = clearers.ToList();
        _logger = logger;
    }

    /// <summary>Deletes all telemetry, once the request names the site.</summary>
    /// <param name="request">The typed host name.</param>
    /// <param name="cancellationToken">Cancels the clear before it starts.</param>
    /// <returns>204 when cleared; 400 when the host name does not match.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClearAsync(ClearTelemetryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var host = Request.Host.Host;

        if (!string.Equals(request.Confirm?.Trim(), host, StringComparison.OrdinalIgnoreCase))
        {
            return Problem(
                title: "The site name does not match.",
                detail: $"Type {host} to clear all telemetry.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        LogClearing(_logger, User.UserId());

        foreach (var clearer in _clearers)
        {
            await clearer.ClearAsync(cancellationToken);
        }

        LogCleared(_logger, User.UserId());

        return NoContent();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} is clearing all telemetry")]
    private static partial void LogClearing(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Security: user {UserId} cleared all telemetry")]
    private static partial void LogCleared(ILogger logger, Guid userId);
}
