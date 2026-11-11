namespace Veyra.Core.Telemetry;

/// <summary>
/// Reports how much disk space stored logs use. The Storage module implements it; the Query module shows it on the
/// home dashboard and projects when the storage budget will be reached.
/// </summary>
public interface ILogStorageUsage
{
    /// <summary>Measures log storage now.</summary>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The size, the budget, and the recent daily growth.</returns>
    Task<LogStorageUsage> GetUsageAsync(CancellationToken cancellationToken);
}
