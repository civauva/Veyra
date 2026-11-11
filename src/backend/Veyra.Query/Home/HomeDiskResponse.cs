namespace Veyra.Query.Home;

/// <summary>A disk watched through host metrics, for the home's outlook.</summary>
/// <param name="Host">The host, from <c>host.name</c>.</param>
/// <param name="Mountpoint">Where the filesystem is mounted.</param>
/// <param name="Ratio">The share in use now, from 0 to 1.</param>
/// <param name="DaysUntilFull">
/// When it is full at the pace of the last day, in days; null when it is not filling or that is more than a year.
/// </param>
public sealed record HomeDiskResponse(string Host, string Mountpoint, double Ratio, double? DaysUntilFull);
