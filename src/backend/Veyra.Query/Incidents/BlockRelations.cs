using Veyra.Core.Alerts;
using Veyra.Core.Metrics;

namespace Veyra.Query.Incidents;

/// <summary>
/// Tells which hosts the blocks run on, from the inventory of blocks that sent metrics (ADR 0022, ADR 0026): the
/// <c>host.name</c> of a service's or database server's series, and a container's host.
/// </summary>
/// <remarks>
/// Every alert that opens asks, so the answer is kept for a minute: hosts change slowly, and a minute-old answer groups
/// alerts as well as a fresh one, without a read of the inventory per alert.
/// </remarks>
/// <param name="search">The stored metrics.</param>
/// <param name="clock">The clock that decides when the kept answer is too old.</param>
internal sealed class BlockRelations(IMetricSearch search, TimeProvider clock) : IBlockRelations
{
    private static readonly TimeSpan _kept = TimeSpan.FromMinutes(1);

    private static readonly AlertBlockKind[] _kinds =
        [AlertBlockKind.Service, AlertBlockKind.Host, AlertBlockKind.Database, AlertBlockKind.Container];

    // Replaced whole, so a reader never sees half of an answer. Two callers may both read when it is too old; the
    // second answer simply replaces the first.
    private volatile Inventory? _inventory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockLink>> ListLinksAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Links;

    /// <summary>Lists the blocks that sent metrics, with when they last did, as kept for a minute.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The blocks.</returns>
    public async Task<IReadOnlyList<MetricBlockSeen>> ListBlocksAsync(CancellationToken cancellationToken) =>
        (await ReadAsync(cancellationToken)).Blocks;

    /// <summary>The links the inventory tells. Pure; tested without a database.</summary>
    /// <param name="blocks">The blocks that sent metrics.</param>
    /// <returns>A link per block and host it runs on.</returns>
    public static IReadOnlyList<BlockLink> Links(IReadOnlyList<MetricBlockSeen> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return
        [
            .. blocks
                .Where(seen => seen.Block.Kind != AlertBlockKind.Host)
                .SelectMany(seen => HostsOf(seen)
                    .Select(host => new BlockLink(seen.Block.Kind, seen.Block.Name, seen.Environment, host)))
                .Distinct(),
        ];
    }

    // A container is listed once per host, with that host; other kinds list every host their series name.
    private static IReadOnlyList<string> HostsOf(MetricBlockSeen seen)
    {
        if (seen.Block.Kind != AlertBlockKind.Container)
        {
            return seen.Hosts;
        }

        return seen.Host is { Length: > 0 } host ? [host] : [];
    }

    private async Task<Inventory> ReadAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (_inventory is { } kept && now - kept.ReadAt < _kept)
        {
            return kept;
        }

        var blocks = await search.ListBlocksAsync(_kinds, now, cancellationToken);
        var fresh = new Inventory(now, blocks, Links(blocks));
        _inventory = fresh;

        return fresh;
    }

    // One read of the inventory, with the links it tells.
    private sealed record Inventory(
        DateTimeOffset ReadAt,
        IReadOnlyList<MetricBlockSeen> Blocks,
        IReadOnlyList<BlockLink> Links);
}
