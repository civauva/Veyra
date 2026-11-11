namespace Veyra.Query.Metrics.Api;

/// <summary>One mounted filesystem of a host.</summary>
/// <param name="Mountpoint">Where it is mounted, for example <c>/</c>.</param>
/// <param name="Device">The device, for example <c>/dev/sda1</c>.</param>
/// <param name="UsedBytes">Space in use.</param>
/// <param name="TotalBytes">Space in use plus free space.</param>
/// <param name="Ratio">The share in use, from 0 to 1.</param>
/// <param name="DaysUntilFull">
/// When it is full at the pace of the last 24 hours, in days; null when it is not filling or that is more than a year.
/// </param>
public sealed record DiskResponse(
    string Mountpoint,
    string? Device,
    double UsedBytes,
    double TotalBytes,
    double Ratio,
    double? DaysUntilFull);
