namespace Veyra.Query.Home;

/// <summary>What the home's dashboards show of the estate, worked out on the server (VEY-68).</summary>
/// <param name="Services">The services, by name.</param>
/// <param name="ServicesWorstFirst">
/// The same services, warnings first, then cautions, then the rest, each by name, for a home that has room for only
/// some of them.
/// </param>
/// <param name="ServiceCounts">How many services are at each health.</param>
/// <param name="Hosts">The hosts that have an open alert or report disks, by name, each with the disk it draws.</param>
/// <param name="HostCounts">How many hosts are at each health.</param>
/// <param name="Databases">The database servers that have an open alert, by name.</param>
/// <param name="DatabaseCounts">How many database servers are at each health.</param>
/// <param name="DiskOutlook">The disks that fill within 30 days and have no open alert.</param>
/// <param name="AlertCounts">How many of the open alerts the home tells are warnings, and how many cautions.</param>
public sealed record HomeEstateResponse(
    IReadOnlyList<BlockResponse> Services,
    IReadOnlyList<BlockResponse> ServicesWorstFirst,
    HealthCountsResponse ServiceCounts,
    IReadOnlyList<HostTileResponse> Hosts,
    HealthCountsResponse HostCounts,
    IReadOnlyList<BlockResponse> Databases,
    HealthCountsResponse DatabaseCounts,
    HomeDiskOutlookResponse DiskOutlook,
    AlertCountsResponse AlertCounts);
