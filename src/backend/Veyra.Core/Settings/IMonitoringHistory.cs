using System.Security.Claims;

namespace Veyra.Core.Settings;

/// <summary>
/// Records changes to monitored systems and agents in the settings history, beside the changes to settings
/// (ADR 0019). The module that keeps the history implements it; the modules that own the systems call it.
/// </summary>
public interface IMonitoringHistory
{
    /// <summary>Records one change, saying who made it.</summary>
    /// <param name="change">What changed, without any values.</param>
    /// <param name="by">The signed-in person who made the change.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the change is recorded.</returns>
    Task RecordAsync(MonitoringChange change, ClaimsPrincipal by, CancellationToken cancellationToken);
}
