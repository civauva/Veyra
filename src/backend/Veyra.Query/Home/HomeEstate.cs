using Veyra.Core.Alerts;

namespace Veyra.Query.Home;

/// <summary>
/// Works out what the home's dashboards show of the estate (VEY-68): the services by name and the worst first, the
/// host tiles with the disk each draws, the disk outlook, and the counts, so the browser only lays them out. Pure.
/// </summary>
internal static class HomeEstate
{
    /// <summary>How many days ahead a filling disk is named, on its host's tile and in the outlook.</summary>
    public const int OutlookDays = 30;

    /// <summary>Builds the estate.</summary>
    /// <param name="blocks">The snapshot's blocks, by kind and then by name.</param>
    /// <param name="disks">The disks from host metrics, the soonest full first.</param>
    /// <param name="open">The open alerts the home tells, the worst first.</param>
    /// <returns>The estate.</returns>
    public static HomeEstateResponse Build(
        IReadOnlyList<BlockResponse> blocks,
        IReadOnlyList<HomeDiskResponse> disks,
        IReadOnlyList<HomeAlertResponse> open)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(disks);
        ArgumentNullException.ThrowIfNull(open);

        var services = blocks.Where(block => block.Kind == "service").ToList();
        var databases = blocks.Where(block => block.Kind == "database").ToList();
        var hosts = Hosts(blocks, disks);

        // OrderBy is stable, so services alike keep their order by name.
        var worstFirst = services.OrderBy(block => Rank(block.Health)).ToList();

        return new HomeEstateResponse(
            services,
            worstFirst,
            Count(services.Select(block => block.Health)),
            hosts,
            Count(hosts.Select(host => host.Health)),
            databases,
            Count(databases.Select(block => block.Health)),
            Outlook(disks, open),
            new AlertCountsResponse(
                open.Count(alert => alert.Severity == AlertSeverity.Warning),
                open.Count(alert => alert.Severity == AlertSeverity.Caution)));
    }

    /// <summary>
    /// Adds the day's totals to the hours: its records and errors, and the average of the hours past, without the
    /// hour still filling when there is any other.
    /// </summary>
    /// <param name="hourly">The hours as read.</param>
    /// <returns>The hours with their totals.</returns>
    public static HourlyResponse Totals(HourlyResponse hourly)
    {
        ArgumentNullException.ThrowIfNull(hourly);

        var observed = hourly.Observed;
        var past = observed.Count > 1 ? observed.Take(observed.Count - 1).ToList() : [.. observed];

        return hourly with
        {
            Records = observed.Sum(bucket => bucket.Total),
            Errors = observed.Sum(bucket => bucket.Errors),
            AverageRecords = past.Count == 0 ? 0 : past.Average(bucket => (double)bucket.Total),
        };
    }

    // The hosts of host blocks and of disks, by name: their worst state, and the disk that fills soonest within the
    // outlook or else their fullest.
    private static List<HostTileResponse> Hosts(
        IReadOnlyList<BlockResponse> blocks,
        IReadOnlyList<HomeDiskResponse> disks)
    {
        var names = blocks
            .Where(block => block.Kind == "host")
            .Select(block => block.Name)
            .Concat(disks.Select(disk => disk.Host))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return
        [
            .. names.Select(name =>
            {
                var healths = blocks
                    .Where(block => block.Kind == "host" && block.Name == name)
                    .Select(block => block.Health)
                    .ToList();
                var health = healths.Contains(BlockHealth.Warning)
                    ? BlockHealth.Warning
                    : healths.Contains(BlockHealth.Caution) ? BlockHealth.Caution : BlockHealth.Ok;
                var own = disks.Where(disk => disk.Host == name).ToList();
                var filling = own
                    .Where(disk => disk.DaysUntilFull <= OutlookDays)
                    .OrderBy(disk => disk.DaysUntilFull)
                    .FirstOrDefault();
                var fullest = own.OrderByDescending(disk => disk.Ratio).FirstOrDefault();

                return new HostTileResponse(name, health, filling ?? fullest, filling is not null);
            }),
        ];
    }

    // The disks that fill within the outlook and have no open alert, which Needs you would tell instead.
    private static HomeDiskOutlookResponse Outlook(
        IReadOnlyList<HomeDiskResponse> disks,
        IReadOnlyList<HomeAlertResponse> open)
    {
        var soon = disks.Where(disk => disk.DaysUntilFull <= OutlookDays).ToList();
        var filling = soon
            .Where(disk => !open.Any(alert =>
                alert.Kind == "host" && alert.Name == disk.Host && alert.Part == disk.Mountpoint))
            .ToList();

        return new HomeDiskOutlookResponse(
            filling,
            disks.Select(disk => disk.Host).Distinct(StringComparer.Ordinal).Count(),
            filling.Count == 0 && soon.Count > 0);
    }

    private static HealthCountsResponse Count(IEnumerable<BlockHealth> healths)
    {
        var all = healths.ToList();

        return new HealthCountsResponse(
            all.Count(health => health == BlockHealth.Warning),
            all.Count(health => health == BlockHealth.Caution),
            all.Count(health => health == BlockHealth.Ok),
            all.Count(health => health == BlockHealth.Unknown));
    }

    private static int Rank(BlockHealth health) =>
        health switch
        {
            BlockHealth.Warning => 0,
            BlockHealth.Caution => 1,
            _ => 2,
        };
}
