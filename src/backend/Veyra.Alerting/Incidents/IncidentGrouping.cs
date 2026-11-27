using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Incidents;

/// <summary>
/// Decides which incident a new alert joins (ADR 0026): an open incident of its environment with an alert on a related
/// block that opened within the grouping window, or none, so the alert opens an incident of its own.
/// </summary>
/// <param name="relations">
/// Which blocks run on which hosts, from the Query module; <see langword="null"/> where it is not registered, as in
/// tests of this module alone, and then only alerts on the same block or a container and its host group.
/// </param>
/// <param name="options">The Alerting settings with the grouping window; the defaults when null.</param>
/// <param name="logger">Receives a failed read of the relations, after which the alert still opens.</param>
internal sealed partial class IncidentGrouping(
    IBlockRelations? relations,
    IOptionsMonitor<AlertingOptions>? options,
    ILogger<IncidentGrouping> logger)
{
    private static readonly AlertingOptions _defaults = new();

    /// <summary>Grouping without relations from series and with the default window.</summary>
    public static IncidentGrouping Alone { get; } = new(null, null, NullLogger<IncidentGrouping>.Instance);

    /// <summary>How close together related alerts must open to share an incident, as set now (ADR 0017).</summary>
    public TimeSpan Window =>
        TimeSpan.FromMinutes(Math.Max(1, (options?.CurrentValue ?? _defaults).Incidents.GroupingMinutes));

    /// <summary>Reads which blocks run on which hosts.</summary>
    /// <remarks>
    /// An alert must open even when the relations cannot be read: a failure is logged, and the alert groups as if no
    /// block ran on any host.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The links; empty when there are none or they could not be read.</returns>
    public async Task<IReadOnlyList<BlockLink>> LinksAsync(CancellationToken cancellationToken)
    {
        if (relations is null)
        {
            return [];
        }

        try
        {
            return await relations.ListLinksAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLinksFailed(exception);

            return [];
        }
    }

    /// <summary>Picks the incident a new alert joins. Pure; tested without a database.</summary>
    /// <param name="alert">The alert that opens.</param>
    /// <param name="candidates">The alerts of the open incidents of its environment.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <param name="window">How long before the alert a related alert may have opened.</param>
    /// <returns>
    /// The incident whose related alert opened last, or <see langword="null"/> when no incident has a related alert
    /// within the window.
    /// </returns>
    public static Guid? Choose(
        AlertState alert,
        IReadOnlyList<IncidentCandidate> candidates,
        IReadOnlyList<BlockLink> links,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => candidate.OpenedAt >= alert.OpenedAt - window)
            .Where(candidate => BlockLinks.IsRelated(alert.Subject, candidate.Subject, alert.Environment, links))
            .OrderByDescending(candidate => candidate.OpenedAt)
            .Select(candidate => (Guid?)candidate.IncidentId)
            .FirstOrDefault();
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not read which blocks run on which hosts; the alert groups by its own block only")]
    private partial void LogLinksFailed(Exception exception);
}
