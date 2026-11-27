namespace Veyra.Core.Alerts;

/// <summary>
/// Decides whether two blocks are related (ADR 0026): the same block, a container and its host, or a service or
/// database server and a host it runs on. Alerting groups alerts with it and Query lists the related components, so
/// both mean the same thing by "related".
/// </summary>
public static class BlockLinks
{
    /// <summary>Whether two alert subjects are related.</summary>
    /// <remarks>
    /// Two services on one host are not related: that they share a machine says nothing about whether one failure
    /// caused the other, and grouping them would merge unrelated incidents.
    /// </remarks>
    /// <param name="first">One subject.</param>
    /// <param name="second">The other.</param>
    /// <param name="environment">The environment both belong to.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <returns><see langword="true"/> when they are related.</returns>
    public static bool IsRelated(
        AlertSubject first,
        AlertSubject second,
        string? environment,
        IReadOnlyList<BlockLink> links)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(links);

        if (SameBlock(first, second))
        {
            return true;
        }

        return (first.Kind, second.Kind) switch
        {
            (AlertBlockKind.Host, not AlertBlockKind.Host) => RunsOn(second, first.Name, environment, links),
            (not AlertBlockKind.Host, AlertBlockKind.Host) => RunsOn(first, second.Name, environment, links),
            _ => false,
        };
    }

    /// <summary>
    /// Whether two subjects are about the same block. A part does not count, except a container's host.
    /// </summary>
    /// <param name="first">One subject.</param>
    /// <param name="second">The other.</param>
    /// <returns><see langword="true"/> when they are the same block.</returns>
    public static bool SameBlock(AlertSubject first, AlertSubject second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Kind != second.Kind || !string.Equals(first.Name, second.Name, StringComparison.Ordinal))
        {
            return false;
        }

        // Containers on two hosts may share a name; the host in the part says which one (ADR 0018).
        return first.Kind != AlertBlockKind.Container
            || string.Equals(first.Part, second.Part, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The hosts a block runs on: a container's from its part, every other kind's from the links.</summary>
    /// <param name="block">The block.</param>
    /// <param name="environment">Its environment.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <returns>The host names, alphabetically, each once; empty for a host.</returns>
    public static IReadOnlyList<string> HostsOf(AlertSubject block, string? environment, IReadOnlyList<BlockLink> links)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(links);

        if (block.Kind == AlertBlockKind.Host)
        {
            return [];
        }

        if (block.Kind == AlertBlockKind.Container)
        {
            return block.Part is { Length: > 0 } host ? [host] : [];
        }

        return
        [
            .. links
                .Where(link => link.Kind == block.Kind
                    && string.Equals(link.Name, block.Name, StringComparison.Ordinal)
                    && Shares(link, environment))
                .Select(link => link.Host)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>The blocks the links say run on a host: services, database servers, and containers.</summary>
    /// <param name="host">The host name.</param>
    /// <param name="environment">The environment.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <returns>The blocks as whole subjects, a container with its host as its part, by kind and then name.</returns>
    public static IReadOnlyList<AlertSubject> RunningOn(
        string host,
        string? environment,
        IReadOnlyList<BlockLink> links)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(links);

        return
        [
            .. links
                .Where(link => string.Equals(link.Host, host, StringComparison.OrdinalIgnoreCase)
                    && Shares(link, environment))
                .Select(link => link.Kind == AlertBlockKind.Container
                    ? AlertSubject.Container(link.Name, link.Host)
                    : new AlertSubject(link.Kind, link.Name))
                .Distinct()
                .OrderBy(subject => subject.Kind)
                .ThenBy(subject => subject.Name, StringComparer.Ordinal),
        ];
    }

    // A block runs on a host when the host is one of its hosts.
    private static bool RunsOn(AlertSubject block, string host, string? environment, IReadOnlyList<BlockLink> links) =>
        HostsOf(block, environment, links).Contains(host, StringComparer.OrdinalIgnoreCase);

    // Series that name no environment belong to every environment, as hosts without one do on the home.
    private static bool Shares(BlockLink link, string? environment) =>
        link.Environment is null
        || environment is null
        || string.Equals(link.Environment, environment, StringComparison.Ordinal);
}
