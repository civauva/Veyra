using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Query.Incidents;

namespace Veyra.Query.Tests.Incidents;

/// <summary>What surrounds an incident, worked out from what was read (ADR 0026). Pure; no database.</summary>
public sealed class IncidentContextTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly BlockLink[] _links =
    [
        new(AlertBlockKind.Service, "checkout", "production", "web-01"),
        new(AlertBlockKind.Database, "microsoft.sql_server sql-prod-01", "production", "web-01"),
        new(AlertBlockKind.Container, "checkout-api", "production", "web-01"),
    ];

    [Fact]
    public void Window_OpenIncident_RunsFromHalfAnHourBeforeToNow()
    {
        var window = IncidentWindow.Of(Incident(null, Alert(AlertSubject.Service("checkout"))), _noon.AddHours(2));

        Assert.Equal((_noon.AddMinutes(-30), _noon.AddHours(2)), (window.From, window.To));
    }

    [Fact]
    public void Window_ResolvedIncident_EndsTenMinutesAfter()
    {
        var incident = Incident(_noon.AddHours(1), Alert(AlertSubject.Service("checkout")));

        var window = IncidentWindow.Of(incident, _noon.AddDays(1));

        Assert.Equal(_noon.AddMinutes(70), window.To);
    }

    [Fact]
    public void Window_ResolvedMomentsAgo_EndsNow()
    {
        var incident = Incident(_noon.AddHours(1), Alert(AlertSubject.Service("checkout")));

        Assert.Equal(_noon.AddMinutes(62), IncidentWindow.Of(incident, _noon.AddMinutes(62)).To);
    }

    [Fact]
    public void Affected_AlertsOnPartsOfOneBlock_CountTheBlockOnce()
    {
        var incident = Incident(
            null,
            Alert(AlertSubject.Host("web-01", "/")),
            Alert(AlertSubject.Host("web-01", "/var")),
            Alert(AlertSubject.Container("checkout-api", "web-01")));

        Assert.Equal(
            [AlertSubject.Host("web-01"), AlertSubject.Container("checkout-api", "web-01")],
            IncidentComponents.Affected(incident));
    }

    [Fact]
    public void TracePath_FailingTrace_ListsTheCriticalPathInOrderWithStatus()
    {
        var path = IncidentPaths.Trace("t1", Trace());

        Assert.Equal(("trace", "t1", null), (path.Source, path.TraceId, path.JobName));
        Assert.Equal(
            [
                ("POST /checkout", "checkout", 100.0, "error", (string?)"payment failed"),
                ("charge", "payments", 70.0, "error", "card declined"),
                ("psp call", "payments", 60.0, "unset", null),
            ],
            path.Steps.Select(step => (step.Name, step.Service, step.DurationMs!.Value, step.Status, step.Detail)));
    }

    [Fact]
    public void TraceException_CauseWithAnExceptionEvent_ReadsItsTypeMessageAndStack()
    {
        var exception = IncidentPaths.TraceException("t1", Trace());

        Assert.NotNull(exception);
        Assert.Equal(
            ("CardDeclinedException", "The card was declined", "at Pay()", "payments", "t1", "trace"),
            (exception.Type, exception.Message, exception.StackTrace, exception.Service, exception.TraceId, exception.Source));
    }

    [Fact]
    public void TraceException_NoFailedSpan_ReturnsNone()
    {
        Assert.Null(IncidentPaths.TraceException("t1", [Span("a", null, "ok", "web", 0, 10, SpanStatus.Ok)]));
    }

    [Fact]
    public void LogException_RecordWithAStackTrace_ReadsItFromTheRecord()
    {
        var records = new[]
        {
            Record("Timeout calling payments", null),
            Record("Timeout calling payments", "at Pay()") with { TraceId = "t9" },
        };

        var exception = IncidentPaths.LogException("Timeout calling payments", records);

        Assert.Equal(
            ("TimeoutException", "Timeout calling payments", "at Pay()", "checkout", "t9", "logs"),
            (exception.Type, exception.Message, exception.StackTrace, exception.Service, exception.TraceId, exception.Source));
    }

    [Fact]
    public void LogException_NoRecordHasAStackTrace_KeepsTheMessage()
    {
        var exception = IncidentPaths.LogException("Timeout calling payments", [Record("Other", null)]);

        Assert.Equal(("Timeout calling payments", null, null), (exception.Message, exception.StackTrace, exception.Service));
    }

    [Fact]
    public void JobPath_FailedRun_ListsItsStepsInOrder()
    {
        var run = _noon.AddMinutes(-5);
        var records = new[]
        {
            Job("nightly-etl", "0", "0", 120, run, "The job failed."),
            Job("nightly-etl", "2", "0", 100, run.AddSeconds(20), "Login failed for user 'etl'."),
            Job("nightly-etl", "1", "1", 20, run, "Done."),
            Job("other", "0", "0", 5, run, "Other failed."),
            Job("nightly-etl", "0", "1", 110, run.AddDays(-1), "Succeeded."),
        };

        var path = IncidentPaths.Job("nightly-etl", records, _noon);

        Assert.NotNull(path);
        Assert.Equal(("job", null, "nightly-etl"), (path.Source, path.TraceId, path.JobName));
        Assert.Equal(
            [("Step 1", 20_000.0, "ok", "Done."), ("Step 2", 100_000.0, "error", "Login failed for user 'etl'.")],
            path.Steps.Select(step => (step.Name, step.DurationMs!.Value, step.Status, step.Detail!)));
    }

    [Fact]
    public void JobRunPath_FailedAgentRun_ListsItsStepsFromTheHistory()
    {
        var start = _noon.AddMinutes(-5);
        var run = AgentRun("nightly-etl", start, 120, "The job failed.");
        var records = new[]
        {
            Job("nightly-etl", "2", "0", 100, start.AddSeconds(20), "Login failed for user 'etl'."),
            Job("nightly-etl", "1", "1", 20, start, "Done."),
        };

        var path = IncidentPaths.JobRun(run, records);

        Assert.Equal(("job", null, "nightly-etl"), (path.Source, path.TraceId, path.JobName));
        Assert.Equal(
            [("Step 1", 20_000.0, "ok", "Done."), ("Step 2", 100_000.0, "error", "Login failed for user 'etl'.")],
            path.Steps.Select(step => (step.Name, step.DurationMs!.Value, step.Status, step.Detail!)));
    }

    [Fact]
    public void JobRunPath_RunWithoutSteps_SaysHowItEnded()
    {
        var path = IncidentPaths.JobRun(AgentRun("nightly-etl", _noon, 42, "The job failed."), []);

        var step = Assert.Single(path.Steps);
        Assert.Equal(("nightly-etl", 42_000.0, "error", "The job failed."), (
            step.Name,
            step.DurationMs!.Value,
            step.Status,
            step.Detail!));
    }

    [Fact]
    public void JobRunPath_TracedRun_ListsTheStepsItKept()
    {
        var run = AgentRun("import", _noon, 8, "2 rows failed") with
        {
            Job = new JobKey(
                JobSource.Trace,
                "production",
                new TelemetryBlock(AlertBlockKind.Service, "billing"),
                "import"),
            TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
            Steps =
            [
                new JobStep("extract", 4, JobRunStatus.Succeeded, null),
                new JobStep("load", 2, JobRunStatus.Failed, "Deadlock victim"),
            ],
        };

        var path = IncidentPaths.JobRun(run, []);

        Assert.Equal(("job", "4bf92f3577b34da6a3ce929d0e0e4736", "import"), (path.Source, path.TraceId, path.JobName));
        Assert.Equal(
            [("extract", "billing", "ok"), ("load", "billing", "error")],
            path.Steps.Select(step => (step.Name, step.Service!, step.Status)));
    }

    [Fact]
    public void JobPath_NoFailedRun_ReturnsNone()
    {
        var records = new[] { Job("nightly-etl", "0", "1", 110, _noon.AddMinutes(-5), "Succeeded.") };

        Assert.Null(IncidentPaths.Job("nightly-etl", records, _noon));
    }

    [Fact]
    public void LogQueryText_ServicesHostsAndEnvironment_ShowsAllTheirWarnings()
    {
        var text = IncidentLogs.QueryText(["checkout", "web 01"], "production");

        Assert.Equal("service:checkout AND service:\"web 01\" AND env:production AND level>=warning", text);
    }

    [Fact]
    public void Components_ServiceAlert_AddsTheHostItRunsOn()
    {
        var incident = Incident(null, Alert(AlertSubject.Service("checkout")));

        var components = IncidentComponents.Of(incident, _links, [], [], Options(), _noon);

        Assert.Equal(
            [("service", "checkout", "affected", true), ("host", "web-01", "host", false)],
            components.Select(component =>
                (component.Block.Kind, component.Block.Name, component.Relation, component.InIncident)));
        Assert.Equal("service:production/checkout", components[0].Block.Id);
    }

    [Fact]
    public void Components_HostAlert_AddsWhatRunsOnIt()
    {
        var incident = Incident(null, Alert(AlertSubject.Host("web-01", "/")));

        var components = IncidentComponents.Of(incident, _links, [], [], Options(), _noon);

        Assert.Equal(
            [
                ("host", "web-01", (string?)null, "affected"),
                ("service", "checkout", null, "runs-on-host"),
                ("database", "microsoft.sql_server sql-prod-01", null, "runs-on-host"),
                ("container", "checkout-api", "web-01", "runs-on-host"),
            ],
            components.Select(component =>
                (component.Block.Kind, component.Block.Name, component.Block.Part, component.Relation)));
    }

    [Fact]
    public void Components_Health_ComesFromOpenAlertsAndSilence()
    {
        var resolvedAlert = Alert(AlertSubject.Service("checkout")) with { ResolvedAt = _noon.AddMinutes(5) };
        var incident = Incident(null, resolvedAlert);
        var open = new[]
        {
            Alert(AlertSubject.Host("web-01", "/")) with { Severity = AlertSeverity.Caution },
            Alert(AlertSubject.Host("web-01", "/var")) with { Severity = AlertSeverity.Warning },
        };
        var seen = new[]
        {
            Seen(AlertBlockKind.Service, "checkout", _noon.AddHours(-3)),
        };

        var components = IncidentComponents.Of(incident, _links, open, seen, Options(), _noon);

        Assert.Equal(
            [("checkout", "silent", false), ("web-01", "warning", false)],
            components.Select(component => (component.Block.Name, component.Health, component.InIncident)));
    }

    [Fact]
    public void AlertWindow_OpenAlert_RunsFromAnHourBeforeToNow()
    {
        var window = IncidentWindow.OfAlert(Alert(AlertSubject.Service("checkout")), _noon.AddHours(2));

        Assert.Equal((_noon.AddHours(-1), _noon.AddHours(2)), (window.From, window.To));
    }

    [Fact]
    public void AlertWindow_ResolvedAlert_EndsWhenItResolved()
    {
        var alert = Alert(AlertSubject.Service("checkout")) with { ResolvedAt = _noon.AddMinutes(25) };

        Assert.Equal(_noon.AddMinutes(25), IncidentWindow.OfAlert(alert, _noon.AddDays(1)).To);
    }

    [Fact]
    public void AlertComponents_DiskAlert_ListsItsHostInTheAlertAndWhatRunsOnIt()
    {
        var alert = Alert(AlertSubject.Host("web-01", "/var")) with { ResolvedAt = _noon.AddMinutes(5) };

        var components = IncidentComponents.OfAlert(alert, _links, [], [], Options(), _noon);

        Assert.Equal(
            [
                ("host", "web-01", "affected", true),
                ("service", "checkout", "runs-on-host", false),
                ("database", "microsoft.sql_server sql-prod-01", "runs-on-host", false),
                ("container", "checkout-api", "runs-on-host", false),
            ],
            components.Select(component =>
                (component.Block.Kind, component.Block.Name, component.Relation, component.InIncident)));
    }

    [Fact]
    public void AlertBlocks_DatabaseAlert_AreItsServerThenItsHost()
    {
        var alert = Alert(AlertSubject.Database("microsoft.sql_server sql-prod-01", "shop"));

        var blocks = IncidentComponents.AroundAlert(alert, _links);

        Assert.Equal(
            [AlertSubject.Database("microsoft.sql_server sql-prod-01"), AlertSubject.Host("web-01")],
            blocks);
    }

    private static Veyra.Query.QueryOptions Options() => new();

    private static IncidentState Incident(DateTimeOffset? resolvedAt, params AlertState[] alerts) =>
        new(Guid.CreateVersion7(), 42, "production", _noon, resolvedAt, alerts);

    private static AlertState Alert(AlertSubject subject) => new(
        Guid.CreateVersion7(),
        "error-rate",
        "production",
        subject,
        AlertSeverity.Warning,
        0.2,
        0.2,
        0.05,
        TimeSpan.FromMinutes(5),
        _noon,
        _noon,
        null);

    private static MetricBlockSeen Seen(AlertBlockKind kind, string name, DateTimeOffset lastSeen) => new(
        new TelemetryBlock(kind, name),
        "production",
        null,
        ["web-01"],
        1,
        new Dictionary<string, string>(),
        lastSeen.AddDays(-1),
        lastSeen);

    // POST /checkout (0-100 ms) calls charge (20-90), which failed with an exception after its psp call (25-85).
    private static Span[] Trace() =>
    [
        Span("a", null, "POST /checkout", "checkout", 0, 100, SpanStatus.Error, "payment failed"),
        Span("b", "a", "charge", "payments", 20, 70, SpanStatus.Error, "card declined", exception: true),
        Span("c", "b", "psp call", "payments", 25, 60, SpanStatus.Unset),
    ];

    private static Span Span(
        string id,
        string? parent,
        string name,
        string service,
        double startMs,
        double durationMs,
        SpanStatus status,
        string? message = null,
        bool exception = false) => new(
        "t1",
        id,
        parent,
        name,
        SpanKind.Internal,
        _noon.AddMilliseconds(startMs),
        TimeSpan.FromMilliseconds(durationMs),
        status,
        message,
        service,
        "production",
        "web-01",
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        exception
            ? [new SpanEvent(_noon.AddMilliseconds(startMs + 1), "exception", new Dictionary<string, string>
            {
                ["exception.type"] = "CardDeclinedException",
                ["exception.message"] = "The card was declined",
                ["exception.stacktrace"] = "at Pay()",
            })]
            : [],
        [],
        SpanDropped.None);

    private static LogRecord Record(string body, string? stackTrace) => new(
        _noon,
        _noon,
        LogSeverity.Error,
        "Error",
        body,
        "checkout",
        "production",
        null,
        null,
        stackTrace is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>
            {
                ["exception.type"] = "TimeoutException",
                ["exception.stacktrace"] = stackTrace,
            },
        new Dictionary<string, string>());

    private static JobRun AgentRun(string job, DateTimeOffset start, double seconds, string message) => new(
        new JobKey(
            JobSource.SqlAgent,
            "production",
            TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01"),
            job),
        "1",
        start,
        seconds,
        JobRunStatus.Failed,
        [],
        null,
        null,
        null,
        null,
        message);

    private static ActivityRecord Job(
        string job,
        string step,
        string status,
        double seconds,
        DateTimeOffset at,
        string message) => new(
        "microsoft.sql_server sql-prod-01",
        "production",
        at,
        "sqlserver.job",
        $"{job}:{step}:{at.UtcTicks}",
        new Dictionary<string, string>
        {
            ["job_name"] = job,
            ["step_id"] = step,
            ["step_name"] = step == "0" ? "(Job outcome)" : $"Step {step}",
            ["run_status"] = status,
            ["duration"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        message);
}
