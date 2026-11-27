using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Testing;
using Xunit.Abstractions;

namespace Veyra.Api.Tests.Query;

/// <summary>
/// The jobs of ADR 0027 against PostgreSQL: runs recognised in traces and read from SQL Server Agent, listed one row
/// per job and opened one job at a time, every filter and figure worked out on the server.
/// </summary>
[Collection(PostgresGroup.Name)]
public class JobsEndpointTests(PostgresFixture postgres, ITestOutputHelper output)
{
    private const string _password = "another long secret";

    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    private static readonly TelemetryBlock _sql = TelemetryBlock.Database("microsoft.sql_server", "SQL-PROD-01");

    [Fact]
    public async Task List_Estate_FailingFirstThenRunningThenByName()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&environment=production");

        Assert.Equal(
            [("Nightly backup", "sqlagent"), ("Index rebuild", "sqlagent"), ("nightly-import", "trace")],
            list!.Items.Select(item => (item.Name, item.Source)));
        Assert.Equal(new JobListFigures(3, 5, 2, 0.6, 1), list.Figures);

        var backup = list.Items[0];
        Assert.Equal(
            new JobBlock("database", _sql.Name, null, $"database:/{_sql.Name}"),
            backup.Block);
        Assert.Equal(("failed", 42d), (backup.LastRun!.Status, backup.LastRun.DurationSeconds!.Value));
        Assert.Equal((2, 1, 0.5), (backup.Runs, backup.Failed, backup.SuccessRatio!.Value));
        Assert.Equal(["succeeded", "failed"], backup.Recent);
        Assert.False(backup.Running);

        var rebuild = list.Items[1];
        Assert.Equal(
            ("running", null, true),
            (rebuild.LastRun!.Status, rebuild.LastRun.DurationSeconds, rebuild.Running));
        Assert.Equal(["running"], rebuild.Recent);

        var import = list.Items[2];
        Assert.Equal(new JobBlock("service", "billing", null, "service:production/billing"), import.Block);
        Assert.Equal(
            ("production", 3, 1, 2.0 / 3),
            (import.Environment, import.Runs, import.Failed, import.SuccessRatio!.Value));
        Assert.Equal(["succeeded", "failed", "succeeded"], import.Recent);
        Assert.Equal(30, import.UsualSeconds);
    }

    [Theory]
    [InlineData("source=trace", "nightly-import")]
    [InlineData("source=sqlagent", "Nightly backup,Index rebuild")]
    [InlineData("q=BACKUP", "Nightly backup")]
    [InlineData("q=billing", "nightly-import")]
    [InlineData("status=failing", "Nightly backup")]
    [InlineData("status=running", "Index rebuild")]
    [InlineData("system=true&q=syspolicy", "syspolicy_purge_history")]
    [InlineData("q=syspolicy", "")]
    public async Task List_Filters_KeepWhatIsAskedFor(string filter, string expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var list = await admin.GetFromJsonAsync<JobList>($"/api/v1/jobs?range=24h&{filter}");

        Assert.Equal(expected, string.Join(",", list!.Items.Select(item => item.Name)));
    }

    [Fact]
    public async Task List_StatusFilter_KeepsTheFiguresOfEveryJob()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);

        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&status=failing");

        Assert.Equal(new JobListFigures(3, 5, 2, 0.6, 1), list!.Figures);
    }

    [Fact]
    public async Task Get_TracedJob_HasItsFiguresDurationsAndRunsWithSteps()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&source=trace");

        var job = await admin.GetFromJsonAsync<JobDetail>($"/api/v1/jobs/{list!.Items[0].Id}?range=24h");

        Assert.Equal(list.Items[0], job!.Job);
        Assert.Equal(new JobFigures(3, 1, 2.0 / 3, 30, job.Figures.P95Seconds, 600), job.Figures);
        Assert.Equal(["succeeded", "failed", "succeeded"], job.Durations.Select(point => point.Status));
        Assert.True(job.Durations[0].At < job.Durations[2].At);

        var failed = job.Runs[1];
        Assert.Equal(("failed", "Deadlock victim", true, Trace(2)), (
            failed.Status,
            failed.Message,
            failed.TraceKept,
            failed.TraceId));
        Assert.Equal((Trace(2), 200L, 250L, 1), (
            failed.RunId,
            failed.RecordsProcessed!.Value,
            failed.RecordsTotal!.Value,
            failed.Retries!.Value));
        Assert.Equal(
            [new JobStep("extract", 10, "succeeded", null), new JobStep("load", 20, "failed", "Deadlock victim")],
            failed.Steps);
        Assert.True(job.Runs[0].StartedAt > job.Runs[2].StartedAt);
    }

    [Fact]
    public async Task Get_SqlAgentJob_ReadsItsStepsFromTheHistory()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&q=Nightly%20backup");

        var job = await admin.GetFromJsonAsync<JobDetail>($"/api/v1/jobs/{list!.Items[0].Id}?range=24h");

        var latest = job!.Runs[0];
        Assert.Equal(("1", "failed", "The job failed.", null, false), (
            latest.RunId,
            latest.Status,
            latest.Message,
            latest.TraceId,
            latest.TraceKept));
        Assert.Equal([new JobStep("Back up", 41, "failed", "Disk full.")], latest.Steps);
        Assert.Equal(0, latest.Retries);
        Assert.Equal(new JobFigures(2, 1, 0.5, 36, job.Figures.P95Seconds, null), job.Figures);
    }

    [Fact]
    public async Task Get_JobWithoutRunsInTheWindow_ShowsItsLatestRun()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&source=trace");

        var job = await admin.GetFromJsonAsync<JobDetail>($"/api/v1/jobs/{list!.Items[0].Id}?range=15m");

        Assert.Equal((0, "succeeded"), (job!.Job.Runs, job.Job.LastRun!.Status));
        Assert.Empty(job.Runs);
        Assert.Empty(job.Durations);
    }

    [Theory]
    [InlineData("/api/v1/jobs/not-a-job", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/jobs/WyJ0cmFjZSIsbnVsbCwiYmlsbGluZyIsIm5vIHN1Y2ggam9iIl0", HttpStatusCode.NotFound)]
    [InlineData("/api/v1/jobs?status=broken", HttpStatusCode.BadRequest)]
    [InlineData("/api/v1/jobs?source=cron", HttpStatusCode.BadRequest)]
    public async Task Request_UnknownJobOrFilter_IsAProblem(string url, HttpStatusCode expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task List_WindowLongerThanThirtyOneDays_IsAProblem()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(
            $"/api/v1/jobs?from={Text(_now.AddDays(-32))}&to={Text(_now)}",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_Viewer_IsAllowedAndSignedOutIsRefused()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var created = await admin.PostAsJsonAsync(
            "/api/v1/users",
            new { email = "ana@example.com", displayName = "Ana", role = "Viewer", password = _password });
        created.EnsureSuccessStatusCode();
        using var viewer = await factory.CreateSignedInClientAsync("ana@example.com", _password);
        using var anonymous = factory.CreateHttpsClient();

        using var allowed = await viewer.GetAsync(new Uri("/api/v1/jobs", UriKind.Relative));
        using var refused = await anonymous.GetAsync(new Uri("/api/v1/jobs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    [Fact]
    public async Task Overview_TracedJob_JoinsThePipelinesWithItsJobId()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h&source=trace");

        var overview = await admin.GetFromJsonAsync<Overview>("/api/v1/overview?range=24h");

        var import = Assert.Single(overview!.Pipelines, pipeline => pipeline.Name == "nightly-import");
        Assert.Equal(("billing", null, list!.Items[0].Id, 3, 1), (
            import.Server,
            import.Engine,
            import.JobId,
            import.Runs,
            import.FailedRuns));
        var jobs = overview.Figures.Jobs;
        Assert.Equal((0.6, 5, 2), (jobs.SucceededRatio!.Value, jobs.Runs, jobs.Failed));
    }

    [Fact]
    public async Task Jobs_Estate_AreReadQuickly()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(factory);
        var list = await admin.GetFromJsonAsync<JobList>("/api/v1/jobs?range=24h");
        var times = new List<double>();

        foreach (var url in new[]
        {
            "/api/v1/jobs?range=1h",
            "/api/v1/jobs?range=30d",
            $"/api/v1/jobs/{list!.Items[0].Id}?range=30d",
            $"/api/v1/jobs/{list.Items[2].Id}?range=30d",
        })
        {
            var watch = Stopwatch.StartNew();
            using var response = await admin.GetAsync(new Uri(url, UriKind.Relative));
            response.EnsureSuccessStatusCode();
            times.Add(watch.Elapsed.TotalMilliseconds);
            output.WriteLine($"{url}: {watch.Elapsed.TotalMilliseconds:F0} ms");
        }

        Assert.All(times, time => Assert.True(time < 2_000, $"A jobs read took {time:F0} ms."));
    }

    private static string Text(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Trace(int number) => number.ToString("x32", CultureInfo.InvariantCulture);

    private static string Id(int number) => number.ToString("x16", CultureInfo.InvariantCulture);

    // A traced job with three runs, the middle one failed with two steps; a SQL Server Agent job with two runs, the
    // latest failed in its backup step; a system job; and a job the agent says runs now.
    private static async Task SeedAsync(VeyraApiFactory factory)
    {
        var spans = new List<Span>();

        for (var run = 1; run <= 3; run++)
        {
            var start = _now.AddHours(-4 + run);
            var failed = run == 2;
            spans.Add(Span(Trace(run), Id(run * 10), Id(run), "extract", start, 10_000, SpanStatus.Ok));
            spans.Add(Span(
                Trace(run),
                Id((run * 10) + 1),
                Id(run),
                "load",
                start.AddSeconds(10),
                20_000,
                failed ? SpanStatus.Error : SpanStatus.Ok) with
            {
                StatusMessage = failed ? "Deadlock victim" : null,
            });
            var status = failed ? SpanStatus.Error : SpanStatus.Ok;
            spans.Add(Span(Trace(run), Id(run), null, "import", start, 30_000, status) with
            {
                StatusMessage = failed ? "Deadlock victim" : null,
                Attributes = new Dictionary<string, string>
                {
                    ["job.name"] = "nightly-import",
                    ["records.processed"] = (run * 100).ToString(CultureInfo.InvariantCulture),
                    ["records.total"] = (run * 125).ToString(CultureInfo.InvariantCulture),
                    ["retry.count"] = failed ? "1" : "0",
                },
            });
        }

        await factory.Services.GetRequiredService<ISpanWriter>().WriteAsync(spans, CancellationToken.None);

        await factory.Services.GetRequiredService<IDatabaseActivityWriter>().WriteAsync(
            [
                Agent("1", "Nightly backup", "0", "0", 42, _now.AddMinutes(-10), "The job failed."),
                Agent("2", "Nightly backup", "1", "0", 41, _now.AddMinutes(-10), "Disk full."),
                Agent("3", "Nightly backup", "0", "1", 30, _now.AddHours(-5), "The job succeeded."),
                Agent("4", "syspolicy_purge_history", "0", "1", 3, _now.AddHours(-2), ""),
            ],
            CancellationToken.None);

        await factory.Services.GetRequiredService<IMetricWriter>().WriteAsync(
            [Running("Index rebuild", 120), Running("Nightly backup", -1)],
            CancellationToken.None);
    }

    private static Span Span(
        string traceId,
        string spanId,
        string? parentId,
        string name,
        DateTimeOffset start,
        int milliseconds,
        SpanStatus status) => new(
        traceId,
        spanId,
        parentId,
        name,
        SpanKind.Internal,
        start,
        TimeSpan.FromMilliseconds(milliseconds),
        status,
        null,
        "billing",
        "production",
        "worker-01",
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "billing" },
        [],
        [],
        SpanDropped.None);

    private static ActivityRecord Agent(
        string key,
        string job,
        string step,
        string status,
        int seconds,
        DateTimeOffset at,
        string message) => new(
        _sql.Name,
        null,
        at,
        "sqlserver.job",
        key,
        new Dictionary<string, string>
        {
            ["job_name"] = job,
            ["step_id"] = step,
            ["step_name"] = step == "0" ? "(Job outcome)" : "Back up",
            ["run_status"] = status,
            ["duration"] = seconds.ToString(CultureInfo.InvariantCulture),
        },
        message);

    private static MetricPoint Running(string job, double seconds) => new(
        "sqlserver.agent.job.running",
        MetricKind.Gauge,
        "s",
        string.Empty,
        MetricTemporality.Unspecified,
        false,
        null,
        null,
        "db-host",
        new Dictionary<string, string> { ["db.system.name"] = _sql.Engine!, ["server.address"] = _sql.Server! },
        new Dictionary<string, string> { ["job_name"] = job },
        _now.AddMinutes(-1),
        null,
        seconds,
        null,
        _sql);

    private sealed record JobList(DateTimeOffset From, DateTimeOffset To, JobListFigures Figures, List<JobItem> Items);

    private sealed record JobListFigures(int Jobs, int Runs, int Failed, double? SuccessRatio, int Running);

    private sealed record JobItem(
        string Id,
        string Name,
        string Source,
        JobBlock Block,
        string? Environment,
        LastRun? LastRun,
        int Runs,
        int Failed,
        double? SuccessRatio,
        double? UsualSeconds,
        List<string> Recent,
        bool Running)
    {
        public bool Equals(JobItem? other) =>
            other is not null && Id == other.Id && Runs == other.Runs && Recent.SequenceEqual(other.Recent);

        public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
    }

    private sealed record JobBlock(string Kind, string Name, string? Part, string Id);

    private sealed record LastRun(DateTimeOffset StartedAt, double? DurationSeconds, string Status);

    private sealed record JobDetail(
        DateTimeOffset From,
        DateTimeOffset To,
        JobItem Job,
        JobFigures Figures,
        List<DurationPoint> Durations,
        List<RunDetail> Runs);

    private sealed record JobFigures(
        int Runs,
        int Failed,
        double? SuccessRatio,
        double? MedianSeconds,
        double? P95Seconds,
        long? RecordsProcessed);

    private sealed record DurationPoint(DateTimeOffset At, double? Seconds, string Status);

    private sealed record RunDetail(
        string RunId,
        DateTimeOffset StartedAt,
        double? DurationSeconds,
        string Status,
        string? Message,
        string? TraceId,
        bool TraceKept,
        long? RecordsProcessed,
        long? RecordsTotal,
        int? Retries,
        List<JobStep> Steps);

    private sealed record JobStep(string Name, double? DurationSeconds, string Status, string? Message);

    private sealed record Overview(OverviewFigures Figures, List<Pipeline> Pipelines);

    private sealed record OverviewFigures(JobsFigure Jobs);

    private sealed record JobsFigure(double? SucceededRatio, int Runs, int Failed);

    private sealed record Pipeline(
        string Name,
        string Server,
        string? Engine,
        DateTimeOffset LastRunAt,
        double DurationSeconds,
        string Status,
        int Runs,
        int FailedRuns,
        string JobId);
}
