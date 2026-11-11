using Veyra.Core.Telemetry;

namespace Veyra.Storage.Logs;

/// <summary>
/// Stands in for <see cref="LogStore"/> when no connection string is configured, so Veyra can start and report the
/// problem through <c>/readyz</c> instead of failing at startup.
/// </summary>
internal sealed class UnconfiguredLogStore : ILogWriter, ILogSearch, ILogRates, ILogStorageUsage
{
    /// <summary>Always fails: there is no database to count in.</summary>
    /// <param name="windowStart">Ignored.</param>
    /// <param name="windowEnd">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<ServiceLogRate>> GetServiceRatesAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to measure.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<LogStorageUsage> GetUsageAsync(CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to write to.</summary>
    /// <param name="records">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task WriteAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to search.</summary>
    /// <param name="query">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to count in.</summary>
    /// <param name="query">Ignored.</param>
    /// <param name="bucketSize">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<LogSummary> SummarizeAsync(LogQuery query, TimeSpan bucketSize, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <summary>Always fails: there is no database to count in.</summary>
    /// <param name="query">Ignored.</param>
    /// <param name="limit">Ignored.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>Never completes successfully.</returns>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public Task<IReadOnlyList<LogMessageGroup>> GroupMessagesAsync(
        LogQuery query,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
