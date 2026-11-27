using Veyra.Core.Activity;

namespace Veyra.Storage.Activity;

/// <summary>
/// Stands in for <see cref="ActivityStore"/> when no database is configured, so the modules can start; every use fails
/// with a message that says what to set.
/// </summary>
internal sealed class UnconfiguredActivityStore : IDatabaseActivityWriter, IDatabaseActivitySearch, IBackupHistory
{
    /// <inheritdoc />
    public Task WriteAsync(IReadOnlyList<DatabaseActivity> records, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<SessionSnapshot?> GetSessionsAsync(
        string block,
        DateTimeOffset at,
        TimeSpan maxAge,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
        string block,
        string kind,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlyCollection<string>? keys,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<ActivityRecord>> ListRecordsMatchingAsync(
        string block,
        string kind,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        ActivityAttributeMatch match,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
        string block,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupDay>> ListDaysAsync(
        string block,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
        string block,
        string queryHash,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
