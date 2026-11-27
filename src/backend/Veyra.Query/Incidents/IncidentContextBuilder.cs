using Microsoft.Extensions.Options;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Query.Incidents.Api;

namespace Veyra.Query.Incidents;

/// <summary>
/// Gathers what surrounds an incident (ADR 0026): the path that failed and its exception, the moments of other
/// signals, charts of the affected blocks, their warnings, and the components around them. The incident itself comes
/// from the Alerting module through <see cref="IIncidentReader"/>.
/// </summary>
/// <remarks>
/// Every read is bounded by the incident's window and its affected blocks, so the context stays within the speed
/// budgets of ADR 0015 however much telemetry is kept. Traces are read a few at a time: at most
/// <see cref="TracesRead"/> failing traces are opened to find one whose cause has an exception.
/// </remarks>
/// <param name="incidents">The incidents.</param>
/// <param name="traces">The stored traces.</param>
/// <param name="logs">The stored logs.</param>
/// <param name="metrics">The stored metrics.</param>
/// <param name="activity">The database servers' records, for job steps, settings, and deadlocks.</param>
/// <param name="jobs">The job runs, for a failed job's run (ADR 0027).</param>
/// <param name="alerts">The alerts, for the health of the components.</param>
/// <param name="relations">Which blocks run on which hosts, and when blocks last sent.</param>
/// <param name="options">The Query settings with how long a block may stay quiet.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class IncidentContextBuilder(
    IIncidentReader incidents,
    ITraceSearch traces,
    ILogSearch logs,
    IMetricSearch metrics,
    IDatabaseActivitySearch activity,
    IJobRuns jobs,
    IAlertStates alerts,
    BlockRelations relations,
    IOptionsMonitor<QueryOptions> options,
    TimeProvider clock)
{
    /// <summary>The most failing traces opened to find one whose cause has an exception.</summary>
    public const int TracesRead = 3;

    // A job's failed run may have started well before its alert opened, as long jobs do.
    private static readonly TimeSpan _jobsBefore = TimeSpan.FromDays(1);

    // The rule of a failed SQL Agent job, whose subject's part is the job (ADR 0018).
    private const string _jobRule = "db-agent-job";

    // Enough error records to find a stack trace of the most frequent message among them.
    private const int _errorRecords = 200;

    private readonly IncidentCharts _charts = new(metrics, logs);
    private readonly IncidentSignals _signals = new(activity, logs);
    private readonly IncidentLogs _logs = new(logs);

    /// <summary>Gathers the context of one incident.</summary>
    /// <param name="id">The incident's ID.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The context, or <see langword="null"/> when there is no incident with this ID.</returns>
    public async Task<IncidentContextResponse?> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await incidents.GetAsync(id, cancellationToken) is not { } incident)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var window = IncidentWindow.Of(incident, now);
        var affected = IncidentComponents.Affected(incident);
        var environment = incident.Environment;

        // The parts read different stores and need nothing of each other, so they are read side by side.
        var path = PathAsync(incident, affected, window, cancellationToken);
        var signals = _signals.ReadAsync(affected, environment, window, cancellationToken);
        var charts = _charts.ReadAsync(affected, environment, window, cancellationToken);
        var records = _logs.ReadAsync(affected, environment, window, cancellationToken);
        var components = ComponentsAsync(incident, now, cancellationToken);

        await Task.WhenAll(path, signals, charts, records, components);

        var (executionPath, exception) = await path;

        return new IncidentContextResponse(
            window.From,
            window.To,
            executionPath,
            exception,
            await signals,
            await charts,
            await records,
            await components);
    }

    // A service's failing trace first; a failed job run when no trace says more; the logs for the exception last.
    private async Task<(ExecutionPathResponse? Path, ObservedExceptionResponse? Exception)> PathAsync(
        IncidentState incident,
        IReadOnlyList<AlertSubject> affected,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var services = affected
            .Where(block => block.Kind == AlertBlockKind.Service)
            .Select(block => block.Name)
            .ToList();
        var environments = incident.Environment is { } environment ? new[] { environment } : null;

        ExecutionPathResponse? path = null;
        ObservedExceptionResponse? exception = null;

        if (services.Count > 0)
        {
            (path, exception) = await TraceAsync(services, environments, window, cancellationToken);
        }

        if (path is null)
        {
            var jobAlerts = incident.Alerts.Where(alert => alert.Rule == _jobRule && alert.Subject.Part is not null);

            foreach (var alert in jobAlerts)
            {
                if (await JobAsync(alert, window, cancellationToken) is { } job)
                {
                    path = job;

                    break;
                }
            }
        }

        if (exception is null && services.Count > 0)
        {
            exception = await LogExceptionAsync(services, environments, window, cancellationToken);
        }

        return (path, exception);
    }

    // The latest failed run of an alert's SQL Agent job, from the job runs (ADR 0027), with its steps from the job
    // history. A run kept before the job runs existed is found in the history alone, as before.
    private async Task<ExecutionPathResponse?> JobAsync(
        AlertState alert,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var name = alert.Subject.Part!;
        var job = new JobKey(
            JobSource.SqlAgent,
            alert.Environment,
            new TelemetryBlock(AlertBlockKind.Database, alert.Subject.Name),
            name);
        var readingRuns = jobs.ListRunsAsync(
            job,
            window.From - _jobsBefore,
            window.To,
            failedOnly: true,
            limit: 1,
            cancellationToken);
        var readingRecords = activity.ListRecordsMatchingAsync(
            alert.Subject.Name,
            IncidentPaths.JobKind,
            window.From - _jobsBefore,
            window.To,
            new ActivityAttributeMatch("job_name", [name], []),
            _errorRecords,
            cancellationToken);

        await Task.WhenAll(readingRuns, readingRecords);

        var records = await readingRecords;

        return await readingRuns is [var run, ..]
            ? IncidentPaths.JobRun(run, records)
            : IncidentPaths.Job(name, records, window.To);
    }

    // The newest failing traces of the affected services; the first whose cause has an exception wins, or else the
    // newest one that could be read.
    private async Task<(ExecutionPathResponse? Path, ObservedExceptionResponse? Exception)> TraceAsync(
        IReadOnlyList<string> services,
        IReadOnlyList<string>? environments,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var failing = await traces.ListAsync(
            new TraceQuery(
                window.From,
                window.To,
                Services: services,
                Environments: environments,
                FailedOnly: true,
                Limit: TracesRead),
            cancellationToken);

        (string TraceId, IReadOnlyList<Span> Spans, ObservedExceptionResponse? Exception)? chosen = null;

        foreach (var trace in failing.Items)
        {
            var spans = await traces.GetTraceAsync(trace.TraceId, trace.Start, cancellationToken);

            if (spans.Count == 0)
            {
                continue;
            }

            var found = IncidentPaths.TraceException(trace.TraceId, spans);
            chosen ??= (trace.TraceId, spans, found);

            if (found is { Type: not null } or { StackTrace: not null })
            {
                chosen = (trace.TraceId, spans, found);

                break;
            }
        }

        return chosen is { } best ? (IncidentPaths.Trace(best.TraceId, best.Spans), best.Exception) : (null, null);
    }

    private async Task<ObservedExceptionResponse?> LogExceptionAsync(
        IReadOnlyList<string> services,
        IReadOnlyList<string>? environments,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        var errors = new LogQuery(
            From: window.From,
            To: window.To,
            MinimumSeverity: LogSeverity.Error,
            Services: services,
            Text: null,
            TraceId: null,
            Limit: _errorRecords,
            After: null,
            Environments: environments);

        var groups = await logs.GroupMessagesAsync(errors, 1, cancellationToken);

        if (groups.Count == 0)
        {
            return null;
        }

        var records = await logs.SearchAsync(errors, cancellationToken);

        return IncidentPaths.LogException(groups[0].Message, [.. records.Items.Select(stored => stored.Record)]);
    }

    private async Task<IReadOnlyList<IncidentComponentResponse>> ComponentsAsync(
        IncidentState incident,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var links = await relations.ListLinksAsync(cancellationToken);
        var seen = await relations.ListBlocksAsync(cancellationToken);
        var current = await alerts.ListAsync(now, cancellationToken);

        return IncidentComponents.Of(incident, links, current, seen, options.CurrentValue, now);
    }
}
