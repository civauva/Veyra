using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Query.Incidents.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Works out the blocks an incident or one alert affects and the blocks related to them, with each one's health now
/// (ADR 0026, ADR 0029). Pure; the builders read what they need.
/// </summary>
internal static class IncidentComponents
{
    /// <summary>The relation of a block an alert of the incident is about.</summary>
    public const string AffectedRelation = "affected";

    /// <summary>The relation of a host an affected block runs on.</summary>
    public const string HostRelation = "host";

    /// <summary>The relation of a block that runs on an affected host.</summary>
    public const string RunsOnHostRelation = "runs-on-host";

    /// <summary>
    /// The blocks the incident's alerts are about, each once, in the order the alerts joined. A part does not make a
    /// block of its own, except a container's host, which tells containers of one name apart (ADR 0018).
    /// </summary>
    /// <param name="incident">The incident.</param>
    /// <returns>The blocks as whole subjects.</returns>
    public static IReadOnlyList<AlertSubject> Affected(IncidentState incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return [.. incident.Alerts.Select(alert => Whole(alert.Subject)).Distinct()];
    }

    /// <summary>
    /// The affected blocks, then the hosts they run on, then what runs on the affected hosts, each once.
    /// </summary>
    /// <param name="incident">The incident.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <param name="alerts">The alerts of every block, of which the open ones set health.</param>
    /// <param name="seen">The blocks that sent metrics and when they last did, for silence.</param>
    /// <param name="options">The Query settings with how long a block may stay quiet.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The components.</returns>
    public static IReadOnlyList<IncidentComponentResponse> Of(
        IncidentState incident,
        IReadOnlyList<BlockLink> links,
        IReadOnlyList<AlertState> alerts,
        IReadOnlyList<MetricBlockSeen> seen,
        QueryOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var listed = Around(Affected(incident), incident.Environment, links);

        return Describe(
            listed,
            incident.Environment,
            alerts,
            seen,
            options,
            now,
            block => incident.Alerts.Any(alert =>
                alert.ResolvedAt is null && BlockLinks.SameBlock(alert.Subject, block)));
    }

    /// <summary>
    /// The block of one alert, then the blocks related to it as for a block of an incident (ADR 0029), with each one's
    /// health now. The alert's own block counts as in the alert, also once the alert resolved, because it is what the
    /// alert is about.
    /// </summary>
    /// <param name="alert">The alert.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <param name="alerts">The alerts of every block, of which the open ones set health.</param>
    /// <param name="seen">The blocks that sent metrics and when they last did, for silence.</param>
    /// <param name="options">The Query settings with how long a block may stay quiet.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The components, the alert's block first.</returns>
    public static IReadOnlyList<IncidentComponentResponse> OfAlert(
        AlertState alert,
        IReadOnlyList<BlockLink> links,
        IReadOnlyList<AlertState> alerts,
        IReadOnlyList<MetricBlockSeen> seen,
        QueryOptions options,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var own = Whole(alert.Subject);
        var listed = Around([own], alert.Environment, links);

        return Describe(
            listed,
            alert.Environment,
            alerts,
            seen,
            options,
            now,
            block => BlockLinks.SameBlock(block, own));
    }

    /// <summary>The block of one alert and the blocks related to it, as <see cref="OfAlert"/> lists them.</summary>
    /// <param name="alert">The alert.</param>
    /// <param name="links">Which blocks run on which hosts.</param>
    /// <returns>The blocks as whole subjects, the alert's block first.</returns>
    public static IReadOnlyList<AlertSubject> AroundAlert(AlertState alert, IReadOnlyList<BlockLink> links)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return [.. Around([Whole(alert.Subject)], alert.Environment, links).Select(entry => entry.Block)];
    }

    // The affected blocks, then the hosts they run on, then what runs on the affected hosts, each once.
    private static List<(AlertSubject Block, string Relation)> Around(
        IReadOnlyList<AlertSubject> affected,
        string? environment,
        IReadOnlyList<BlockLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var listed = new List<(AlertSubject Block, string Relation)>();

        void Add(AlertSubject block, string relation)
        {
            if (!listed.Any(entry => BlockLinks.SameBlock(entry.Block, block)))
            {
                listed.Add((block, relation));
            }
        }

        foreach (var block in affected)
        {
            Add(block, AffectedRelation);
        }

        foreach (var block in affected)
        {
            foreach (var host in BlockLinks.HostsOf(block, environment, links))
            {
                Add(AlertSubject.Host(host), HostRelation);
            }
        }

        foreach (var host in affected.Where(block => block.Kind == AlertBlockKind.Host))
        {
            foreach (var block in BlockLinks.RunningOn(host.Name, environment, links))
            {
                Add(block, RunsOnHostRelation);
            }
        }

        return listed;
    }

    // Each listed block with its health now and whether the incident or alert counts it in.
    private static List<IncidentComponentResponse> Describe(
        List<(AlertSubject Block, string Relation)> listed,
        string? environment,
        IReadOnlyList<AlertState> alerts,
        IReadOnlyList<MetricBlockSeen> seen,
        QueryOptions options,
        DateTimeOffset now,
        Func<AlertSubject, bool> inIncident)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(seen);
        ArgumentNullException.ThrowIfNull(options);

        var open = alerts.Where(alert => alert.ResolvedAt is null && alert.Environment == environment).ToList();

        return
        [
            .. listed.Select(entry => new IncidentComponentResponse(
                IncidentBlockResponse.Of(entry.Block, environment),
                Health(entry.Block, open, seen, environment, options, now),
                inIncident(entry.Block),
                entry.Relation)),
        ];
    }

    /// <summary>A subject as its whole block: the part only for a container, where it names the host.</summary>
    /// <param name="subject">The subject.</param>
    /// <returns>The block.</returns>
    public static AlertSubject Whole(AlertSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return subject.Kind == AlertBlockKind.Container ? subject : subject with { Part = null };
    }

    // The worst open alert decides, as on the home (ADR 0007); without one, a block that stopped sending is silent.
    private static string Health(
        AlertSubject block,
        IReadOnlyList<AlertState> open,
        IReadOnlyList<MetricBlockSeen> seen,
        string? environment,
        QueryOptions options,
        DateTimeOffset now)
    {
        var worst = open
            .Where(alert => BlockLinks.SameBlock(alert.Subject, block))
            .Select(alert => (AlertSeverity?)alert.Severity)
            .Max();

        if (worst is { } severity)
        {
            return severity == AlertSeverity.Warning ? "warning" : "caution";
        }

        var lastSeen = seen
            .Where(entry => entry.Block.Kind == block.Kind
                && string.Equals(entry.Block.Name, block.Name, StringComparison.Ordinal)
                && (block.Kind != AlertBlockKind.Container
                    || string.Equals(entry.Host, block.Part, StringComparison.OrdinalIgnoreCase))
                && (entry.Environment is null || environment is null || entry.Environment == environment))
            .Select(entry => (DateTimeOffset?)entry.LastSeen)
            .Max();
        var quiet = block.Kind == AlertBlockKind.Service ? options.SilentAfterServices : options.SilentAfterHosts;

        return lastSeen is { } last && now - last > quiet ? "silent" : "ok";
    }
}
