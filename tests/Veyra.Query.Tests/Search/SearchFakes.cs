using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Search;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Tests.Search;

/// <summary>Finds the blocks and operations a test sets by name, and can hold a kind back until released.</summary>
internal sealed class FakeSignalSearch : ISignalSearch
{
    public List<FoundBlock> Blocks { get; } = [];

    public List<FoundOperation> Operations { get; } = [];

    /// <summary>Kinds whose search waits until the token is cancelled, as a slow query would.</summary>
    public HashSet<AlertBlockKind> Slow { get; } = [];

    public List<NameSearch> OperationSearches { get; } = [];

    public async Task<IReadOnlyList<FoundBlock>> FindBlocksAsync(
        NameSearch search,
        IReadOnlyList<AlertBlockKind> kinds,
        CancellationToken cancellationToken)
    {
        if (kinds.Any(Slow.Contains))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return Blocks
            .Where(block => kinds.Contains(block.Kind))
            .Where(block => Shown(block).Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            .Take(search.Limit)
            .ToList();
    }

    public Task<IReadOnlyList<FoundOperation>> FindOperationsAsync(
        NameSearch search,
        CancellationToken cancellationToken)
    {
        OperationSearches.Add(search);

        return Task.FromResult<IReadOnlyList<FoundOperation>>(Operations
            .Where(operation => operation.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    private static string Shown(FoundBlock block) =>
        block.Database ?? (block.Kind == AlertBlockKind.Database ? block.Name.Split(' ')[1] : block.Name);
}

/// <summary>Counts the records a test sets per part of a window, and can be slow after the first part.</summary>
internal sealed class FakeWordCounts : ILogSearch
{
    public long PerPart { get; set; }

    /// <summary>Whether every part after the first waits until the token is cancelled.</summary>
    public bool SlowAfterFirst { get; set; }

    public List<LogQuery> Counted { get; } = [];

    public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public async Task<LogSummary> SummarizeAsync(
        LogQuery query,
        TimeSpan bucketSize,
        CancellationToken cancellationToken)
    {
        if (SlowAfterFirst && Counted.Count > 0)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        Counted.Add(query);

        return new LogSummary(bucketSize, [new LogBucket(query.From, PerPart, 0)], []);
    }

    public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
        LogQuery query,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Answers the figures a test sets; everything else is not needed by the search.</summary>
internal sealed class FakeMetricHealth : IMetricHealth
{
    public List<DiskHealth> Disks { get; } = [];

    public List<ServiceLatency> Latencies { get; } = [];

    public List<DatabaseHealth> Databases { get; } = [];

    public List<ContainerHealth> Containers { get; } = [];

    public Task<IReadOnlyList<DiskHealth>> ListDisksAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DiskHealth>>(Disks);

    public Task<IReadOnlyList<ServiceLatency>> ListLatenciesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ServiceLatency>>(Latencies);

    public Task<IReadOnlyList<DatabaseHealth>> ListDatabasesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DatabaseHealth>>(Databases);

    public Task<IReadOnlyList<ContainerHealth>> ListContainersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContainerHealth>>(Containers);

    public Task<IReadOnlyList<DatabaseMeasure>> ListDatabaseMeasuresAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ServerError>> ListServerErrorsAsync(
        string server,
        string measure,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DiskHistory> ReadDiskAsync(
        string host,
        string mountpoint,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<LatencyHistory> ReadLatencyAsync(
        string service,
        string? environment,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
