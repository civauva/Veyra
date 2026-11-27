using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Query.Blocking;
using Veyra.Query.Incidents.Api;
using Veyra.Query.Server;

namespace Veyra.Query.Incidents;

/// <summary>
/// Reads the moments of other signals in an incident's window (ADR 0026): the failed SQL Agent job runs, configuration
/// changes, and deadlocks of the affected database servers, and the first error record of each affected service.
/// </summary>
/// <param name="activity">The database servers' records.</param>
/// <param name="logs">The stored logs.</param>
internal sealed class IncidentSignals(IDatabaseActivitySearch activity, ILogSearch logs)
{
    // Snapshots keep each setting once a day, so the day before the window holds the value a change started from.
    private static readonly TimeSpan _settingsBefore = TimeSpan.FromDays(2);

    // Bounds on what one server can add to the timeline; the window is short, so these are rarely reached.
    private const int _maxJobRecords = 500;
    private const int _maxSnapshots = 5_000;
    private const int _maxDeadlocks = 50;

    /// <summary>Reads the moments.</summary>
    /// <param name="blocks">The affected blocks.</param>
    /// <param name="environment">The incident's environment.</param>
    /// <param name="window">The incident's window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The moments, oldest first.</returns>
    public async Task<IReadOnlyList<IncidentSignalResponse>> ReadAsync(
        IReadOnlyList<AlertSubject> blocks,
        string? environment,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(window);

        var signals = new List<IncidentSignalResponse>();

        foreach (var block in blocks)
        {
            var shown = IncidentBlockResponse.Of(block, environment);

            if (block.Kind == AlertBlockKind.Database)
            {
                signals.AddRange(await DatabaseAsync(block.Name, shown, window, cancellationToken));
            }
            else if (block.Kind == AlertBlockKind.Service
                && await FirstErrorAsync(block.Name, environment, window, cancellationToken) is { } first)
            {
                signals.Add(new IncidentSignalResponse(
                    first.Timestamp,
                    "first-error",
                    shown,
                    $"First error of {block.Name}",
                    first.Body));
            }
        }

        return [.. signals.OrderBy(signal => signal.At)];
    }

    private async Task<List<IncidentSignalResponse>> DatabaseAsync(
        string server,
        IncidentBlockResponse shown,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var jobs = activity.ListRecordsAsync(
            server,
            IncidentPaths.JobKind,
            window.From,
            window.To,
            null,
            _maxJobRecords,
            cancellationToken);
        var snapshots = activity.ListRecordsAsync(
            server,
            ConfigurationViewBuilder.SnapshotKind,
            window.From - _settingsBefore,
            window.To,
            null,
            _maxSnapshots,
            cancellationToken);
        var deadlocks = activity.ListRecordsAsync(
            server,
            BlockingViewBuilder.DeadlockKind,
            window.From,
            window.To,
            null,
            _maxDeadlocks,
            cancellationToken);

        await Task.WhenAll(jobs, snapshots, deadlocks);

        var signals = new List<IncidentSignalResponse>();

        signals.AddRange((await jobs)
            .Where(record => record.Attributes.GetValueOrDefault("step_id") == "0"
                && record.Attributes.GetValueOrDefault("run_status") == "0")
            .DistinctBy(record => record.Key)
            .Select(record => new IncidentSignalResponse(
                record.SampledAt,
                "job-failed",
                shown,
                $"SQL Agent job {record.Attributes.GetValueOrDefault("job_name")} failed",
                string.IsNullOrEmpty(record.Body) ? null : record.Body)));

        var settings = await snapshots;

        if (settings.Count > 0)
        {
            signals.AddRange(ConfigurationViewBuilder.Build(settings, window.To).Changes
                .Where(change => change.At >= window.From && change.At <= window.To)
                .Select(change => new IncidentSignalResponse(
                    change.At,
                    "config-change",
                    shown,
                    $"{change.Item} changed",
                    change.From.Length == 0 ? $"now {change.To}" : $"{change.From} → {change.To}")));
        }

        signals.AddRange((await deadlocks)
            .Select(record => BlockingViewBuilder.Deadlock(record.Key, record.SampledAt, record.Body))
            .Select(deadlock => new IncidentSignalResponse(
                deadlock.At,
                "deadlock",
                shown,
                "Deadlock",
                deadlock.Summary)));

        return signals;
    }

    private async Task<LogRecord?> FirstErrorAsync(
        string service,
        string? environment,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var page = await logs.SearchAsync(
            new LogQuery(
                From: window.From,
                To: window.To,
                MinimumSeverity: LogSeverity.Error,
                Services: [service],
                Text: null,
                TraceId: null,
                Limit: 1,
                After: null,
                Environments: environment is null ? null : [environment],
                OldestFirst: true),
            cancellationToken);

        return page.Items.Count == 0 ? null : page.Items[0].Record;
    }
}
