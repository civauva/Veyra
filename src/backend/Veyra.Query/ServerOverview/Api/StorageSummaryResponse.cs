namespace Veyra.Query.ServerOverview.Api;

/// <summary>A SQL Server's storage at a glance (ADR 0025).</summary>
/// <param name="TotalBytes">The data and log files of every database together now.</param>
/// <param name="Growth24hBytes">How much they grew over the last day.</param>
/// <param name="Growth7dBytes">How much they grew over the last 7 days.</param>
/// <param name="Growth30dBytes">How much they grew over the last 30 days.</param>
/// <param name="Line">The total per day over the last 30 days, the oldest first.</param>
/// <param name="Largest">The eight largest databases.</param>
/// <param name="Volumes">The volumes that hold database files.</param>
public sealed record StorageSummaryResponse(
    long? TotalBytes,
    long? Growth24hBytes,
    long? Growth7dBytes,
    long? Growth30dBytes,
    IReadOnlyList<long?> Line,
    IReadOnlyList<LargestDatabaseResponse> Largest,
    IReadOnlyList<OverviewVolumeResponse> Volumes);
