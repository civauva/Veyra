using Veyra.Core.Alerts;

namespace Veyra.Core.Search;

/// <summary>
/// Finds things by name across the stored signals, for the search in the top bar (ADR 0022). The Storage module
/// implements it; the Query module ranks, completes, and serves what it finds.
/// </summary>
public interface ISignalSearch
{
    /// <summary>
    /// Finds services, hosts, containers, database servers, and databases whose name holds the text, from the metric
    /// series and the log counts of the window.
    /// </summary>
    /// <remarks>
    /// A database server is matched by its server, a database by its own name. Matches come exact first, then those
    /// that start with the text, then the rest, each by name, so the first <see cref="NameSearch.Limit"/> are the best.
    /// </remarks>
    /// <param name="search">The text, the window, the environment, and the most to return.</param>
    /// <param name="kinds">The kinds of block to look in.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The blocks found, best first.</returns>
    Task<IReadOnlyList<FoundBlock>> FindBlocksAsync(
        NameSearch search,
        IReadOnlyList<AlertBlockKind> kinds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts the traces of the window whose root operation's name holds the text, per operation and service.
    /// </summary>
    /// <remarks>Ranked as <see cref="FindBlocksAsync"/> ranks names.</remarks>
    /// <param name="search">The text, the window, the environment, and the most to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The operations found, best first.</returns>
    Task<IReadOnlyList<FoundOperation>> FindOperationsAsync(NameSearch search, CancellationToken cancellationToken);
}
