using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.Tests.MetricRules;

/// <summary>Returns whatever figures a test sets, and remembers the periods it was asked for.</summary>
internal sealed class FakeMetricHealth : IMetricHealth
{
    public IReadOnlyList<DiskHealth> Disks { get; set; } = [];

    public IReadOnlyList<ServiceLatency> Latencies { get; set; } = [];

    public DiskHistory DiskHistory { get; set; } = new(TimeSpan.FromHours(1), []);

    public LatencyHistory LatencyHistory { get; set; } = new(TimeSpan.FromMinutes(1), [], []);

    public List<(string Name, string? Detail, DateTimeOffset Start, DateTimeOffset End)> Reads { get; } = [];

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Disks);
    }

    public Task<IReadOnlyList<ServiceLatency>> ListLatenciesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Latencies);
    }

    public Task<DiskHistory> ReadDiskAsync(
        string host,
        string mountpoint,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        Reads.Add((host, mountpoint, periodStart, periodEnd));

        return Task.FromResult(DiskHistory);
    }

    public Task<LatencyHistory> ReadLatencyAsync(
        string service,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        Reads.Add((service, environment, periodStart, periodEnd));

        return Task.FromResult(LatencyHistory);
    }
}
