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

    /// <summary>
    /// Lists the latest changes to one monitored system, newest first, for the page that shows it (ADR 0028).
    /// </summary>
    /// <param name="subject">What kind of system it is.</param>
    /// <param name="name">Its name. A system renamed since keeps its older changes under its old name.</param>
    /// <param name="limit">The most changes to list.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The changes, newest first.</returns>
    Task<IReadOnlyList<MonitoringHistoryEntry>> ListAsync(
        MonitoringSubject subject,
        string name,
        int limit,
        CancellationToken cancellationToken);
}
