using Veyra.Agents.Agents;
using Veyra.Agents.Api;
using Veyra.Agents.Collector;
using Veyra.Agents.Probes;
using Veyra.Agents.SqlServers;
using Veyra.Core;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Settings;
using Veyra.Core.Telemetry;

namespace Veyra.Agents.Integrations;

/// <summary>
/// What the Integrations screens show (ADR 0028): each monitored SQL Server as an integration, and its package page.
/// </summary>
/// <remarks>
/// The Agents module owns integrations: their connection, modules, template versions, probes, and Collector. The
/// page's other figures belong to other modules and are read through Core: the metric series and statement statistics
/// through <see cref="IMetricSearch"/> (the Storage module), the history through <see cref="IMonitoringHistory"/> and
/// the saved settings through <see cref="ISavedSettings"/> (the Access module), and the channels through
/// <see cref="INotificationChannels"/> (the Alerting module). The Query module's statement views are not used: the
/// page needs only the agent's own statements, summed per read, which the stored series give directly.
/// </remarks>
/// <param name="overview">The servers with their states.</param>
/// <param name="agents">The agents that read them.</param>
/// <param name="probes">The servers' last validations.</param>
/// <param name="signals">The agents' own reader errors.</param>
/// <param name="metrics">The servers' stored metrics.</param>
/// <param name="history">The settings history of each server.</param>
/// <param name="settings">Which settings an Admin saved.</param>
/// <param name="channels">The notification channels.</param>
/// <param name="clock">The current time.</param>
internal sealed class IntegrationService(
    AgentsOverview overview,
    AgentStore agents,
    ProbeService probes,
    SqlServerSignals signals,
    IMetricSearch metrics,
    IMonitoringHistory history,
    ISavedSettings settings,
    INotificationChannels channels,
    TimeProvider clock)
{
    /// <summary>The kind of a SQL Server integration.</summary>
    public const string SqlServerKind = "sqlserver";

    /// <summary>The metric the permissions read sends: 1 for each permission the login lacks.</summary>
    public const string MissingPermission = "sqlserver.login.permission.missing";

    // The most changes the page lists.
    private const int _historyLimit = 20;

    // The most series one page reads: a busy server's statements make most of them.
    private const int _seriesLimit = 5_000;

    // The permissions read runs every 5 minutes; a missing permission counts when its last report is this recent.
    private static readonly TimeSpan _permissionsWithin = TimeSpan.FromMinutes(15);

    private static readonly string[] _statementMetrics =
        ["sqlserver.statement.elapsed", "sqlserver.statement.executions"];

    /// <summary>Lists every integration with its state.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The integrations, by name.</returns>
    public async Task<IReadOnlyList<IntegrationItemResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await overview.ListSqlServersAsync(cancellationToken)).Select(Item).ToList();

    /// <summary>Finds one integration as the list shows it.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The integration, or <see langword="null"/> when no server has the ID.</returns>
    public async Task<IntegrationItemResponse?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        (await overview.ListSqlServersAsync(cancellationToken))
            .Where(view => view.Server.Id == id)
            .Select(Item)
            .SingleOrDefault();

    /// <summary>The modules of the SQL Server template, for the setup wizard.</summary>
    /// <returns>The modules, in order.</returns>
    public static IReadOnlyList<ModuleCatalogResponse> Modules() =>
        SqlServerModules.All
            .Select(module => new ModuleCatalogResponse(
                module.Id,
                module.Name,
                module.Description,
                module.DefaultIntervalSeconds,
                module.Choices,
                module.Required))
            .ToList();

    /// <summary>Builds an integration's package page.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="window">The period its figures cover.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page, or <see langword="null"/> when no server has the ID.</returns>
    public async Task<IntegrationDetailResponse?> DetailAsync(
        Guid id,
        IntegrationWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);

        if ((await overview.ListSqlServersAsync(cancellationToken)).SingleOrDefault(view => view.Server.Id == id)
            is not { } view)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var server = view.Server;
        var agent = await agents.FindAsync(server.AgentId, cancellationToken);
        var block = TelemetryBlock.Database(SqlServerSignals.SqlServerEngine, server.Name);
        var series = await metrics.FindSeriesAsync(
            new MetricSeriesQuery(SeenSince: window.From, Limit: _seriesLimit, Block: block),
            cancellationToken);
        var inWindow = series.Where(one => one.FirstSeen < window.To).ToList();
        var buckets = inWindow.Count == 0
            ? []
            : await metrics.ReadBucketsAsync(
                inWindow.Select(one => one.Id).ToList(),
                window.From,
                window.To,
                window.Bucket,
                cancellationToken);
        var errors = await signals.ReadErrorsAsync(now, cancellationToken);
        var probe = await probes.LatestForServerAsync(server.Id, cancellationToken);

        return new IntegrationDetailResponse(
            Item(view),
            await PackageAsync(server, agent, block, now, cancellationToken),
            Collector(server, agent, now),
            new ValidationResponse(probe is null ? null : Probe(probe)),
            ModuleFigures(server, inWindow, buckets, window),
            Checks(server, inWindow, buckets, errors, now),
            await NextStepsAsync(cancellationToken),
            (await history.ListAsync(MonitoringSubject.SqlServer, server.Name, _historyLimit, cancellationToken))
                .Select(Change)
                .ToList());
    }

    /// <summary>A probe as the REST API answers it.</summary>
    /// <param name="probe">The probe.</param>
    /// <returns>The answer, without its password.</returns>
    public static ProbeResponse Probe(SqlProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        return new ProbeResponse(
            probe.Id,
            probe.State,
            probe.StartedAt,
            probe.FinishedAt,
            probe.State == ProbeState.Failed
                ? new ProbeErrorResponse(probe.Failure ?? ProbeFailure.Other, probe.Message ?? "")
                : null,
            probe.State == ProbeState.Succeeded ? probe.Result : null);
    }

    private static IntegrationItemResponse Item(SqlServerView view) =>
        new(
            view.Server.Id,
            SqlServerKind,
            view.Server.Name,
            view.Server.Environment,
            new IntegrationAgentResponse(view.Server.AgentId, view.AgentName),
            view.Server.Version,
            SqlServerTemplates.Current.Version,
            view.Status.State,
            view.Server.Choices,
            view.Server.CreatedAt,
            view.Server.ChangedAt);

    private async Task<PackageResponse> PackageAsync(
        SqlServer server,
        Agent? agent,
        TelemetryBlock block,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var template = SqlServerTemplates.Resolve(server.TemplateVersion);

        return new PackageResponse(
            SqlServerTemplates.PackageName,
            server.Version,
            SqlServerTemplates.Current.Version,
            server.Version == SqlServerTemplates.Previous.Version ? null : SqlServerTemplates.Previous.Version,
            server.TemplateSetAt ?? server.CreatedAt,
            server.TemplateSetBy,
            agent?.Report.Version,
            template.CollectorCompatibility,
            template.MinimumSqlServer,
            server.Authentication == SqlServerAuthentication.Integrated ? null : server.Login,
            server.Authentication,
            await LoginLacksAsync(block, now, cancellationToken));
    }

    // The permissions the login lacked at the agent's last check, from the permissions read's metric.
    private async Task<IReadOnlyList<string>> LoginLacksAsync(
        TelemetryBlock block,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now - _permissionsWithin;
        var found = await metrics.FindSeriesAsync(
            new MetricSeriesQuery(SeenSince: since, Names: [MissingPermission], Block: block),
            cancellationToken);

        if (found.Count == 0)
        {
            return [];
        }

        var latest = (await metrics.ReadBucketsAsync(
                found.Select(one => one.Id).ToList(),
                since,
                now,
                _permissionsWithin,
                cancellationToken))
            .GroupBy(bucket => bucket.SeriesId)
            .ToDictionary(group => group.Key, group => group.MaxBy(bucket => bucket.LastTimestamp)!.LastValue);

        return found
            .Where(one => latest.GetValueOrDefault(one.Id) >= 1)
            .Select(one => one.Attributes.GetValueOrDefault("permission"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static CollectorResponse Collector(SqlServer server, Agent? agent, DateTimeOffset now)
    {
        var report = agent?.Report ?? AgentReport.None;

        return new CollectorResponse(
            server.AgentId,
            agent?.Name ?? "",
            report.Healthy,
            report.StartedAt,
            report.StartedAt is { } started && now > started ? (now - started).TotalSeconds : null,
            report.LastSeenAt,
            report.ConfigState switch
            {
                AgentConfigState.Applied => "applied",
                AgentConfigState.Applying => "applying",
                AgentConfigState.Failed => "failed",
                _ => "unknown",
            },
            report.ConfigError);
    }

    // Each module's series and points over the window. A metric the template's queries name belongs to their
    // receiver's module; every other metric of the server comes from the sqlserver receiver, which is Core.
    private static List<ModuleFiguresResponse> ModuleFigures(
        SqlServer server,
        IReadOnlyList<MetricSeries> series,
        IReadOnlyList<MetricBucket> buckets,
        IntegrationWindow window)
    {
        var modules = CollectorConfig.MetricModules(server);
        var moduleOf = series.ToDictionary(
            one => one.Id,
            one => modules.GetValueOrDefault(one.Name) ?? SqlServerModules.Core);
        var byModule = buckets
            .GroupBy(bucket => moduleOf.GetValueOrDefault(bucket.SeriesId) ?? SqlServerModules.Core)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        return SqlServerModules.All
            .Select(module =>
            {
                var choice = SqlServerModules.ChoiceOf(server.Choices, module);
                var own = byModule.GetValueOrDefault(module.Id) ?? [];
                var perBucket = own
                    .GroupBy(bucket => (int)((bucket.Start - window.From) / window.Bucket))
                    .ToDictionary(group => group.Key, group => group.Sum(bucket => (long)bucket.Points));

                return new ModuleFiguresResponse(
                    module.Id,
                    module.Name,
                    choice.Enabled,
                    choice.IntervalSeconds,
                    own.Select(bucket => bucket.SeriesId).Distinct().Count(),
                    own.Sum(bucket => (long)bucket.Points),
                    Enumerable.Range(0, window.Points)
                        .Select(index => perBucket.TryGetValue(index, out var points) ? (double?)points : null)
                        .ToList());
            })
            .ToList();
    }

    // Each read the agent runs, with SQL Server's time for it from the agent's own statements, which each minute's
    // statistics mark agent='true' and whose text starts with the read's veyra-agent name.
    private static List<CheckResponse> Checks(
        SqlServer server,
        IReadOnlyList<MetricSeries> series,
        IReadOnlyList<MetricBucket> buckets,
        IReadOnlyList<ReaderError> errors,
        DateTimeOffset now)
    {
        var totals = buckets
            .GroupBy(bucket => bucket.SeriesId)
            .ToDictionary(group => group.Key, group => group.Sum(bucket => bucket.Total ?? 0));
        var statements = series
            .Where(one => _statementMetrics.Contains(one.Name)
                && one.Attributes.GetValueOrDefault("agent") == "true")
            .GroupBy(one => PlatformReads.QueryNameOf(one.Attributes.GetValueOrDefault("query_text") ?? ""))
            .Where(group => group.Key.Length > 0)
            .ToDictionary(
                group => group.Key,
                group => (
                    Seconds: group.Where(one => one.Name == _statementMetrics[0])
                        .Sum(one => totals.GetValueOrDefault(one.Id)),
                    Runs: (long)group.Where(one => one.Name == _statementMetrics[1])
                        .Sum(one => totals.GetValueOrDefault(one.Id))),
                StringComparer.Ordinal);
        var suffix = CollectorConfig.ComponentSuffix(server.Id);
        var failing = errors
            .Where(error => error.ServerId == server.Id
                && error.At > server.ChangedAt
                && now - error.At <= SqlServerStatuses.ErrorsWithin
                && error.Component is not null)
            .Select(error => error.Component!)
            .ToHashSet(StringComparer.Ordinal);

        return CollectorConfig.Reads(server)
            .Select(read =>
            {
                var measured = statements.TryGetValue(read.Name, out var found) && found.Runs > 0;

                return new CheckResponse(
                    read.Name,
                    read.Module,
                    measured ? found.Seconds / found.Runs : null,
                    measured ? found.Runs : null,
                    !failing.Contains($"{read.Receiver}/{suffix}"));
            })
            .ToList();
    }

    // What the installation still lacks: thresholds of the database rules left at their defaults, no channel to send
    // alerts through, and retention left at its defaults.
    private async Task<List<NextStepResponse>> NextStepsAsync(CancellationToken cancellationToken)
    {
        var thresholds = await settings.ListSavedKeysAsync(VeyraOptionNames.Alerting + ":Database", cancellationToken);
        var storage = await settings.ListSavedKeysAsync(VeyraOptionNames.Storage + ":", cancellationToken);
        var enabled = await channels.CountEnabledAsync(cancellationToken);

        return
        [
            new NextStepResponse(
                "thresholds",
                "Review the database alert thresholds; they are at their defaults until an Admin saves one.",
                thresholds.Count > 0,
                "/settings?section=Rules"),
            new NextStepResponse(
                "channel",
                "Add a notification channel, so alerts reach someone.",
                enabled > 0,
                "/notifications"),
            new NextStepResponse(
                "retention",
                "Check how long Veyra keeps data; retention is at its defaults until an Admin saves it.",
                storage.Any(key => key.EndsWith("RetentionDays", StringComparison.OrdinalIgnoreCase)),
                "/settings?section=Data"),
        ];
    }

    private static HistoryEntryResponse Change(MonitoringHistoryEntry entry) =>
        new(
            entry.At,
            entry.By,
            entry.Fields.Contains(SqlServerService.VersionField) && entry.Kind == MonitoringChangeKind.Changed
                ? "version"
                : entry.Kind switch
                {
                    MonitoringChangeKind.Added => "added",
                    MonitoringChangeKind.Removed => "removed",
                    _ => "changed",
                },
            entry.From,
            entry.To);
}
