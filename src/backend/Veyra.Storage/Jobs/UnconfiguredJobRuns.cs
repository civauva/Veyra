using Veyra.Core.Jobs;

namespace Veyra.Storage.Jobs;

/// <summary>
/// Stands in for <see cref="JobRunStore"/> when no database is configured, so the modules can start; every use fails
/// with a message that says what to set.
/// </summary>
internal sealed class UnconfiguredJobRuns : IJobRuns
{
    /// <inheritdoc />
    public Task<IReadOnlyList<JobSummary>> SummarizeAsync(JobQuery query, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<JobRun>> ListRunsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        bool failedOnly,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<JobDuration>> ListDurationsAsync(
        JobKey job,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
