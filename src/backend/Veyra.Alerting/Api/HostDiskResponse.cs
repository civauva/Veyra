namespace Veyra.Alerting.Api;

/// <summary>One disk of a host, beside a disk alert.</summary>
/// <param name="Mountpoint">The disk's mount point.</param>
/// <param name="Ratio">The share in use, from 0 to 1.</param>
/// <param name="DaysUntilFull">
/// When it is full at the pace of the last day; <see langword="null"/> when not filling.
/// </param>
public sealed record HostDiskResponse(string Mountpoint, double Ratio, double? DaysUntilFull);
