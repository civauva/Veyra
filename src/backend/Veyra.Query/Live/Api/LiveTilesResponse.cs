using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Live.Api;

/// <summary>The numbers at the top of the Live tab, each of which opens what it counts (ADR 0020).</summary>
/// <param name="BatchRequests">Batch requests per second now, or <see langword="null"/> when none arrived.</param>
/// <param name="BatchRequestsLine">Batch requests per second over the last 15 minutes.</param>
/// <param name="Blocked">Sessions waiting for another session now.</param>
/// <param name="BlockedLine">Blocked sessions over the last 15 minutes.</param>
/// <param name="LongestRequestSeconds">The longest running request now, or 0.</param>
/// <param name="OldestTransactionSeconds">The oldest open transaction now, or <see langword="null"/>.</param>
/// <param name="OldestTransactionSession">The session that holds it.</param>
/// <param name="GrantsWaiting">Requests waiting for memory to run.</param>
/// <param name="TempdbFreeRatio">The share of tempdb's data files still free, from 0 to 1.</param>
/// <param name="TempdbSizeBytes">The size of tempdb's data files.</param>
public sealed record LiveTilesResponse(
    double? BatchRequests,
    ChartLineResponse BatchRequestsLine,
    int Blocked,
    ChartLineResponse BlockedLine,
    double LongestRequestSeconds,
    double? OldestTransactionSeconds,
    int? OldestTransactionSession,
    int GrantsWaiting,
    double? TempdbFreeRatio,
    long TempdbSizeBytes)
{
    /// <summary>
    /// The session that runs the longest request, or null when nothing runs. The tiles are the whole server's, so
    /// it can be one a filter leaves out of the list (VEY-66).
    /// </summary>
    public int? LongestRequestSession { get; init; }
}
