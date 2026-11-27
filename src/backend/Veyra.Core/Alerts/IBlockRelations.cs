namespace Veyra.Core.Alerts;

/// <summary>
/// Tells which hosts the blocks run on (ADR 0026). The Query module implements it from the series it already reads;
/// the Alerting module groups related alerts into incidents with it, without referencing Query.
/// </summary>
public interface IBlockRelations
{
    /// <summary>Lists every block that runs on a host, as its series say.</summary>
    /// <remarks>
    /// It is read each time an alert opens, so implementations answer from memory and refresh now and then: a link a
    /// minute old groups as well as a fresh one.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The links, in no particular order.</returns>
    Task<IReadOnlyList<BlockLink>> ListLinksAsync(CancellationToken cancellationToken);
}
