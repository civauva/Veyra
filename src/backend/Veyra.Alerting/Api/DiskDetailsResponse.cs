using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.Api;

/// <summary>
/// The disk of a disk alert: how full it is now, its use over the last week, and the host's other disks.
/// </summary>
/// <param name="Mountpoint">The disk's mount point.</param>
/// <param name="Ratio">The share in use now; <see langword="null"/> when the host no longer reports the disk.</param>
/// <param name="UsedBytes">The bytes in use now.</param>
/// <param name="TotalBytes">The disk's size.</param>
/// <param name="DaysUntilFull">
/// When it is full at the pace of the last day; <see langword="null"/> when it is not filling or not reported.
/// </param>
/// <param name="BucketSeconds">The width of each point of the chart.</param>
/// <param name="From">The start of the chart: a week before the alert opened.</param>
/// <param name="To">The end of the chart: now, or a day after the alert resolved.</param>
/// <param name="Points">The share in use per point, oldest first.</param>
/// <param name="Disks">Every disk of the host as it is now, fullest first, this one included.</param>
public sealed record DiskDetailsResponse(
    string Mountpoint,
    double? Ratio,
    double? UsedBytes,
    double? TotalBytes,
    double? DaysUntilFull,
    int BucketSeconds,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<DiskUsagePoint> Points,
    IReadOnlyList<HostDiskResponse> Disks);
