using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Tests.Home;

/// <summary>Returns the rates a test sets for each window length, keyed by how far the window reaches back.</summary>
internal sealed class FakeLogRates : ILogRates
{
    public Dictionary<TimeSpan, IReadOnlyList<ServiceLogRate>> ByWindow { get; } = [];

    public Task<IReadOnlyList<ServiceLogRate>> GetServiceRatesAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        Task.FromResult(ByWindow.GetValueOrDefault(windowEnd - windowStart) ?? []);
}

/// <summary>Answers summaries with hourly buckets a test fills in, and remembers every query.</summary>
internal sealed class FakeLogSearch : ILogSearch
{
    public List<(LogQuery Query, TimeSpan BucketSize)> Summaries { get; } = [];

    /// <summary>Record counts per bucket start. Buckets without an entry are empty.</summary>
    public Dictionary<DateTimeOffset, (long Total, long Errors)> Hours { get; } = [];

    public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken)
    {
        Summaries.Add((query, bucketSize));

        var buckets = new List<LogBucket>();

        for (var start = query.From; start < query.To; start += bucketSize)
        {
            var (total, errors) = Hours.GetValueOrDefault(start);
            buckets.Add(new LogBucket(start, total, errors));
        }

        return Task.FromResult(new LogSummary(bucketSize, buckets, []));
    }

    public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
        LogQuery query,
        int limit,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>Returns the alerts a test sets, filtered the way the real store filters them.</summary>
internal sealed class FakeAlertStates : IAlertStates
{
    public List<AlertState> Alerts { get; } = [];

    public DateTimeOffset? LastResolvedSince { get; private set; }

    public Task<IReadOnlyList<AlertState>> ListAsync(DateTimeOffset resolvedSince, CancellationToken cancellationToken)
    {
        LastResolvedSince = resolvedSince;

        return Task.FromResult<IReadOnlyList<AlertState>>(
            Alerts.Where(alert => alert.ResolvedAt is null || alert.ResolvedAt >= resolvedSince).ToList());
    }
}

/// <summary>Returns the storage usage a test sets.</summary>
internal sealed class FakeLogStorageUsage : ILogStorageUsage
{
    public LogStorageUsage Usage { get; set; } = new(1024, null, 14, null);

    public Task<LogStorageUsage> GetUsageAsync(CancellationToken cancellationToken) => Task.FromResult(Usage);
}
