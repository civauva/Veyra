namespace Veyra.Core.Metrics.Health;

/// <summary>One watched disk of a host as it is now, with its trend over the last day.</summary>
/// <param name="Host">The host, from <c>host.name</c>.</param>
/// <param name="Environment">The host's environment, or <see langword="null"/> when its metrics name none.</param>
/// <param name="Mountpoint">Where the file system is mounted.</param>
/// <param name="UsedBytes">The bytes in use.</param>
/// <param name="TotalBytes">The bytes in use and free together.</param>
/// <param name="Ratio">The share in use, from 0 to 1.</param>
/// <param name="DaysUntilFull">
/// When it is full at the pace of the last day, in days; <see langword="null"/> when it is not filling, or not within
/// a year, or the day holds too few samples to tell.
/// </param>
public sealed record DiskHealth(
    string Host,
    string? Environment,
    string Mountpoint,
    double UsedBytes,
    double TotalBytes,
    double Ratio,
    double? DaysUntilFull);
