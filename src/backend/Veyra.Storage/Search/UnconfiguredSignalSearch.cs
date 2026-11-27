using Veyra.Core.Alerts;
using Veyra.Core.Search;

namespace Veyra.Storage.Search;

/// <summary>
/// Stands in for <see cref="SignalSearchStore"/> when no database is configured, so the modules can start; every use
/// fails with a message that says what to set.
/// </summary>
internal sealed class UnconfiguredSignalSearch : ISignalSearch
{
    /// <inheritdoc />
    public Task<IReadOnlyList<FoundBlock>> FindBlocksAsync(
        NameSearch search,
        IReadOnlyList<AlertBlockKind> kinds,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);

    /// <inheritdoc />
    public Task<IReadOnlyList<FoundOperation>> FindOperationsAsync(
        NameSearch search,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException(StorageHealthCheck.NotConfiguredMessage);
}
