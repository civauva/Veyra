using Veyra.Core.Metrics;

namespace Veyra.Query.Metrics;

/// <summary>
/// The metric and attribute names Veyra reads, from the Collector's <c>hostmetrics</c> receiver and the OpenTelemetry
/// semantic conventions. Where the conventions renamed an attribute, both names are read.
/// </summary>
internal static class MetricNames
{
    /// <summary>CPU time per CPU and state, a cumulative sum in seconds.</summary>
    public const string CpuTime = "system.cpu.time";

    /// <summary>Memory per state, in bytes.</summary>
    public const string Memory = "system.memory.usage";

    /// <summary>Filesystem space per mount point and state, in bytes.</summary>
    public const string Filesystem = "system.filesystem.usage";

    /// <summary>The load averages.</summary>
    public const string Load1 = "system.cpu.load_average.1m";

    /// <summary>The five-minute load average.</summary>
    public const string Load5 = "system.cpu.load_average.5m";

    /// <summary>The fifteen-minute load average.</summary>
    public const string Load15 = "system.cpu.load_average.15m";

    /// <summary>Network bytes per device and direction, a cumulative sum.</summary>
    public const string NetworkIo = "system.network.io";

    /// <summary>Disk bytes per device and direction, a cumulative sum.</summary>
    public const string DiskIo = "system.disk.io";

    /// <summary>HTTP server request durations, a histogram in seconds.</summary>
    public const string RequestDuration = "http.server.request.duration";

    /// <summary>Process CPU time of .NET, a cumulative sum in seconds.</summary>
    public const string DotnetCpuTime = "dotnet.process.cpu.time";

    /// <summary>Process CPU time of other runtimes.</summary>
    public const string ProcessCpuTime = "process.cpu.time";

    /// <summary>How many CPUs a .NET process may use.</summary>
    public const string DotnetCpuCount = "dotnet.process.cpu.count";

    /// <summary>The working set of a .NET process, in bytes.</summary>
    public const string DotnetWorkingSet = "dotnet.process.memory.working_set";

    /// <summary>The memory of a process of other runtimes, in bytes.</summary>
    public const string ProcessMemory = "process.memory.usage";

    /// <summary>.NET garbage collections, a cumulative count.</summary>
    public const string DotnetGcCollections = "dotnet.gc.collections";

    /// <summary>Work items waiting in the .NET thread pool.</summary>
    public const string DotnetThreadPoolQueue = "dotnet.thread_pool.queue.length";

    /// <summary>Threads in the .NET thread pool.</summary>
    public const string DotnetThreadPoolThreads = "dotnet.thread_pool.thread.count";

    /// <summary>Every metric a host view reads.</summary>
    public static readonly string[] Host = [CpuTime, Memory, Filesystem, Load1, Load5, Load15, NetworkIo, DiskIo];

    /// <summary>Every metric a service view reads.</summary>
    public static readonly string[] Service =
    [
        RequestDuration,
        DotnetCpuTime,
        ProcessCpuTime,
        DotnetCpuCount,
        DotnetWorkingSet,
        ProcessMemory,
        DotnetGcCollections,
        DotnetThreadPoolQueue,
        DotnetThreadPoolThreads,
    ];

    /// <summary>The first of several attribute names a series has.</summary>
    /// <param name="series">The series.</param>
    /// <param name="keys">The names, newest convention last is fine; the first present wins.</param>
    /// <returns>The value, or null when none is present.</returns>
    public static string? Attribute(MetricSeries series, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(series);

        foreach (var key in keys)
        {
            if (series.Attributes.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
