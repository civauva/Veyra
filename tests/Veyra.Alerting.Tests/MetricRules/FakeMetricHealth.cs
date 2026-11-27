using Veyra.Core.Metrics.Health;

namespace Veyra.Alerting.Tests.MetricRules;

/// <summary>Returns whatever figures a test sets, and remembers the periods it was asked for.</summary>
internal sealed class FakeMetricHealth : IMetricHealth
{
    public IReadOnlyList<DiskHealth> Disks { get; set; } = [];

    public IReadOnlyList<ServiceLatency> Latencies { get; set; } = [];

    public IReadOnlyList<DatabaseHealth> Databases { get; set; } = [];

    public IReadOnlyList<DatabaseMeasure> Measures { get; set; } = [];

    public IReadOnlyList<ContainerHealth> Containers { get; set; } = [];

    public DiskHistory DiskHistory { get; set; } = new(TimeSpan.FromHours(1), []);

    public LatencyHistory LatencyHistory { get; set; } = new(TimeSpan.FromMinutes(1), [], []);

    public List<(string Name, string? Detail, DateTimeOffset Start, DateTimeOffset End)> Reads { get; } = [];

    public int ListCalls { get; private set; }

    public Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Disks);
    }

    public Task<IReadOnlyList<DatabaseMeasure>> ListDatabaseMeasuresAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult(Measures);

    public Task<IReadOnlyList<DatabaseHealth>> ListDatabasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Databases);
    }

    public Task<IReadOnlyList<ContainerHealth>> ListContainersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ListCalls++;

        return Task.FromResult(Containers);
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

    public IReadOnlyList<ServerError> ServerErrors { get; set; } = [];

    public List<(string Server, string Measure, DateTimeOffset Start, DateTimeOffset End)> ErrorReads { get; } = [];

    public Task<IReadOnlyList<ServerError>> ListServerErrorsAsync(
        string server,
        string measure,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken)
    {
        ErrorReads.Add((server, measure, periodStart, periodEnd));

        return Task.FromResult(ServerErrors);
    }
}
