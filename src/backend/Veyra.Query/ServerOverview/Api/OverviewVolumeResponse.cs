namespace Veyra.Query.ServerOverview.Api;

/// <summary>A volume that holds database files, as the Storage tab shows it (ADR 0025).</summary>
/// <param name="Name">The volume, such as <c>D:\</c> or <c>/var/opt/mssql</c>.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="FreeBytes">Its free space.</param>
/// <param name="FreeRatio">Its free share, 0 to 1.</param>
/// <param name="DaysUntilFull">Days until it is full at the pace of the last week; null when it is not filling.</param>
public sealed record OverviewVolumeResponse(
    string Name,
    long SizeBytes,
    long FreeBytes,
    double FreeRatio,
    double? DaysUntilFull);
