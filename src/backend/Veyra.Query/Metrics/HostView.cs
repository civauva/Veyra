using Veyra.Core.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Reads one host's metrics from the Collector's <c>hostmetrics</c> receiver: CPU, memory, disks, network, and load.
/// </summary>
internal static class HostView
{
    // Filesystems that hold no data of their own; people do not mean them when they ask whether a disk is full.
    private static readonly HashSet<string> _pseudoFilesystems = new(StringComparer.Ordinal)
    {
        "tmpfs", "devtmpfs", "overlay", "squashfs", "nsfs", "proc", "sysfs", "devfs", "autofs", "ramfs",
    };

    // A disk that fills more slowly than this is steady.
    private const double _minimumDailyGrowth = 0.0005;

    // An OS name longer than this is a kernel description, too long for a card.
    private const int _maxOsLength = 32;

    /// <summary>The numbers of a host's overview card.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="window">The host's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <param name="day">The host's filesystem series over the last day, for the disk trend.</param>
    /// <returns>The card.</returns>
    public static HostCardResponse Card(string host, SeriesData window, SeriesData recent, SeriesData day)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recent);

        var any = window.Series.Concat(recent.Series).ToList();
        var resource = any.FirstOrDefault()?.ResourceAttributes ?? new Dictionary<string, string>();
        var disks = Disks(recent, day);
        var cpus = window.Named(MetricNames.CpuTime)
            .Select(series => MetricNames.Attribute(series, "cpu", "cpu.logical_number"))
            .Where(cpu => cpu is not null)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return new HostCardResponse(
            Name: host,
            Environment: any.Select(series => series.Environment).FirstOrDefault(value => value is not null),
            Os: OsName(resource),
            Cpus: cpus == 0 ? null : cpus,
            MemoryBytes: recent.Latest(MemoryTotal(recent)),
            LastSeen: recent.LastSent(any, window.Window.End),
            CpuRatio: recent.Average(CpuBusy(recent)),
            MemoryRatio: recent.Latest(MemoryShare(recent, "used")),
            Load: recent.Latest(recent.Levels(recent.Named(MetricNames.Load1))),
            FullestDisk: disks.FirstOrDefault(),
            FillingDisk: disks
                .Where(disk => disk.DaysUntilFull is <= 30)
                .OrderBy(disk => disk.DaysUntilFull)
                .FirstOrDefault(),
            Cpu: window.Window.Line("cpu", CpuBusy(window)));
    }

    /// <summary>A host's metrics over a window.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="window">The host's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <param name="day">The host's filesystem series over the last day, for the disk trend.</param>
    /// <returns>The detail.</returns>
    public static HostMetricsResponse Detail(string host, SeriesData window, SeriesData recent, SeriesData day)
    {
        ArgumentNullException.ThrowIfNull(window);

        var lines = window.Window;
        var cpu = CpuByState(window);

        return new HostMetricsResponse(
            Card(host, window, recent, day),
            lines.Range,
            (int)lines.Bucket.TotalSeconds,
            [
                lines.Line("total", CpuBusy(window)),
                lines.Line("user", cpu.GetValueOrDefault("user") ?? []),
                lines.Line("system", cpu.GetValueOrDefault("system") ?? []),
                lines.Line("wait", cpu.GetValueOrDefault("wait") ?? []),
            ],
            [
                lines.Line("used", MemoryShare(window, "used")),
                lines.Line("cache", MemoryShare(window, "buffered", "cached", "slab_reclaimable")),
            ],
            Disks(recent, day),
            [
                lines.Line("received", ByDirection(window, MetricNames.NetworkIo, "receive", skipLoopback: true)),
                lines.Line("sent", ByDirection(window, MetricNames.NetworkIo, "transmit", skipLoopback: true)),
            ],
            [
                lines.Line("read", ByDirection(window, MetricNames.DiskIo, "read", skipLoopback: false)),
                lines.Line("written", ByDirection(window, MetricNames.DiskIo, "write", skipLoopback: false)),
            ],
            [
                lines.Line("1m", window.Levels(window.Named(MetricNames.Load1))),
                lines.Line("5m", window.Levels(window.Named(MetricNames.Load5))),
                lines.Line("15m", window.Levels(window.Named(MetricNames.Load15))),
            ]);
    }

    /// <summary>The share of CPU time not idle, per bucket.</summary>
    /// <param name="data">The host's series.</param>
    /// <returns>The share from 0 to 1 per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> CpuBusy(SeriesData data)
    {
        // The idle state is already a share of all CPUs' time, so busy is what is left of it.
        var idle = CpuByState(data).GetValueOrDefault("idle") ?? [];
        var total = data.Rates(data.Named(MetricNames.CpuTime));

        return total
            .Where(pair => pair.Value > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => Math.Clamp(1 - idle.GetValueOrDefault(pair.Key), 0, 1));
    }

    // The share of CPU time in each state, per bucket. Every state that is not user, system, idle, or wait counts as
    // part of the total only.
    private static Dictionary<string, Dictionary<DateTimeOffset, double>> CpuByState(SeriesData data)
    {
        var cpu = data.Named(MetricNames.CpuTime).ToList();
        var total = data.Rates(cpu);

        return cpu
            .GroupBy(series => MetricNames.Attribute(series, "state", "cpu.mode") ?? "other", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => data.Rates(group)
                    .Where(pair => total.GetValueOrDefault(pair.Key) > 0)
                    .ToDictionary(pair => pair.Key, pair => pair.Value / total[pair.Key]),
                StringComparer.Ordinal);
    }

    private static Dictionary<DateTimeOffset, double> MemoryTotal(SeriesData data) =>
        data.Levels(data.Named(MetricNames.Memory));

    private static Dictionary<DateTimeOffset, double> MemoryShare(SeriesData data, params string[] states)
    {
        var memory = data.Named(MetricNames.Memory).ToList();
        var total = data.Levels(memory);
        var part = data.Levels(memory.Where(series =>
            states.Contains(MetricNames.Attribute(series, "state", "system.memory.state"), StringComparer.Ordinal)));

        return total
            .Where(pair => pair.Value > 0)
            .ToDictionary(pair => pair.Key, pair => part.GetValueOrDefault(pair.Key) / pair.Value);
    }

    private static Dictionary<DateTimeOffset, double> ByDirection(
        SeriesData data,
        string name,
        string direction,
        bool skipLoopback) =>
        data.Rates(data.Named(name).Where(series =>
            MetricNames.Attribute(series, "direction", "network.io.direction", "disk.io.direction") == direction
            && !(skipLoopback && MetricNames.Attribute(series, "device", "network.interface.name") == "lo")));

    // Every real filesystem with its latest usage and, from the day's trend, when it is full. Fullest first.
    private static List<DiskResponse> Disks(SeriesData recent, SeriesData day)
    {
        var disks = new List<DiskResponse>();

        foreach (var mount in Mounts(recent))
        {
            var used = recent.Latest(recent.Levels(mount.Where(series => State(series) == "used")));
            var free = recent.Latest(recent.Levels(mount.Where(series => State(series) == "free")));

            if (used is not { } usedBytes || free is not { } freeBytes || usedBytes + freeBytes <= 0)
            {
                continue;
            }

            var ratio = usedBytes / (usedBytes + freeBytes);
            var trend = Mounts(day).FirstOrDefault(other => other.Key == mount.Key);

            disks.Add(new DiskResponse(
                mount.Key,
                mount.Select(series => MetricNames.Attribute(series, "device", "system.device")).FirstOrDefault(),
                usedBytes,
                usedBytes + freeBytes,
                ratio,
                trend is null ? null : DaysUntilFull(day, trend, ratio)));
        }

        // A device mounted at several places, as bind mounts are, is one disk: keep its shortest mount point.
        return disks
            .GroupBy(disk => disk.Device ?? disk.Mountpoint, StringComparer.Ordinal)
            .Select(device => device
                .OrderBy(disk => disk.Mountpoint.Length)
                .ThenBy(disk => disk.Mountpoint, StringComparer.Ordinal)
                .First())
            .OrderByDescending(disk => disk.Ratio)
            .ToList();
    }

    /// <summary>The share of one disk in use, per bucket.</summary>
    /// <param name="data">The host's filesystem series.</param>
    /// <param name="mountpoint">The disk's mount point.</param>
    /// <returns>The share from 0 to 1 per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> DiskShare(SeriesData data, string mountpoint)
    {
        ArgumentNullException.ThrowIfNull(data);

        var mount = Mounts(data).FirstOrDefault(group => group.Key == mountpoint)?.ToList() ?? [];
        var used = data.Levels(mount.Where(series => State(series) == "used"));
        var free = data.Levels(mount.Where(series => State(series) == "free"));

        return used
            .Where(pair => free.ContainsKey(pair.Key) && pair.Value + free[pair.Key] > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value / (pair.Value + free[pair.Key]));
    }

    /// <summary>
    /// A short OS name for the card. The Collector's <c>os.description</c> is a distribution with its code name, or,
    /// where it cannot read one (in a container), the whole kernel description; then the OS type is shorter.
    /// </summary>
    private static string? OsName(IReadOnlyDictionary<string, string> resource)
    {
        var description = resource.GetValueOrDefault("os.description");
        var type = resource.GetValueOrDefault("os.type");

        if (description is { Length: > 0 })
        {
            var codeName = description.IndexOf(" (", StringComparison.Ordinal);
            var name = codeName > 0 ? description[..codeName] : description;

            if (name.Length <= _maxOsLength)
            {
                return name;
            }
        }

        return type is { Length: > 0 } ? char.ToUpperInvariant(type[0]) + type[1..] : null;
    }

    // Read-only file systems, such as CD images, cannot fill up, and pseudo file systems hold no data of their own.
    private static IEnumerable<IGrouping<string, MetricSeries>> Mounts(SeriesData data) =>
        data.Named(MetricNames.Filesystem)
            .Where(series => !_pseudoFilesystems.Contains(
                MetricNames.Attribute(series, "type", "system.filesystem.type") ?? string.Empty))
            .Where(series => MetricNames.Attribute(series, "mode", "system.filesystem.mode") != "ro")
            .GroupBy(
                series => MetricNames.Attribute(series, "mountpoint", "system.filesystem.mountpoint") ?? "?",
                StringComparer.Ordinal);

    private static string? State(MetricSeries series) =>
        MetricNames.Attribute(series, "state", "system.filesystem.state");

    // Fits a straight line to the hourly share in use and says when it reaches 100 %.
    private static double? DaysUntilFull(SeriesData day, IEnumerable<MetricSeries> mount, double current)
    {
        var series = mount.ToList();
        var used = day.Levels(series.Where(one => State(one) == "used"));
        var free = day.Levels(series.Where(one => State(one) == "free"));
        var samples = used
            .Where(pair => free.ContainsKey(pair.Key) && pair.Value + free[pair.Key] > 0)
            .Select(pair => (
                Days: (pair.Key - day.Window.Start).TotalDays,
                Ratio: pair.Value / (pair.Value + free[pair.Key])))
            .ToList();

        if (samples.Count < 6)
        {
            return null;
        }

        var meanX = samples.Average(sample => sample.Days);
        var meanY = samples.Average(sample => sample.Ratio);
        var spread = samples.Sum(sample => (sample.Days - meanX) * (sample.Days - meanX));

        if (spread <= 0)
        {
            return null;
        }

        var slope = samples.Sum(sample => (sample.Days - meanX) * (sample.Ratio - meanY)) / spread;

        if (slope < _minimumDailyGrowth)
        {
            return null;
        }

        var days = (1 - current) / slope;

        return days > 365 ? null : Math.Round(days, 1);
    }
}
