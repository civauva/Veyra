using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Activity;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Storage.Activity;
using Veyra.Storage.Jobs;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Jobs;

/// <summary>
/// Job runs (ADR 0027): the span writer recognises a traced run by its root span's job name, the activity writer a SQL
/// Server Agent run by its history row of step 0, both in the batch's transaction, and the store adds them up per job.
/// </summary>
[Collection(PostgresGroup.Name)]
public class JobRunStoreTests(PostgresFixture postgres)
{
    private const string _server = "microsoft.sql_server sql-prod-01";

    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    // A window ending here holds the runs that started at noon.
    private static readonly DateTimeOffset _later = _noon.AddMinutes(1);

    private static readonly JobKey _import = new(
        JobSource.Trace,
        "production",
        new TelemetryBlock(AlertBlockKind.Service, "billing"),
        "nightly-import");

    private static readonly JobKey _backup = new(
        JobSource.SqlAgent,
        null,
        TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01"),
        "Nightly backup");

    [Fact]
    public async Task WriteAsync_RootSpanWithJobName_KeepsARunWithItsStepsRecordsAndRetries()
    {
        await using var database = await CreateAsync();
        var spans = Spans(database);
        var trace = Trace(1);

        // The SDK exports children as they end, so the first step arrives before its root.
        await spans.WriteAsync([Span(trace, Id(2), Id(1), "extract", _noon.AddSeconds(1), 4_000)], default);
        await spans.WriteAsync(
            [
                Span(trace, Id(3), Id(1), "load", _noon.AddSeconds(5), 2_000, SpanStatus.Error) with
                {
                    StatusMessage = "Deadlock victim",
                },
                Span(trace, Id(4), Id(3), "INSERT invoices", _noon.AddSeconds(5), 1_000),
                Span(trace, Id(1), null, "import", _noon, 8_000, SpanStatus.Error) with
                {
                    StatusMessage = "2 rows failed",
                    Attributes = new Dictionary<string, string>
                    {
                        ["job.name"] = "nightly-import",
                        ["job.records.processed"] = "1200",
                        ["records.total"] = "1250",
                        ["retry.count"] = "2",
                    },
                },
            ],
            default);

        var run = Assert.Single(await Store(database).ListRunsAsync(_import, _noon, _later, false, 10, default));

        Assert.Equal(
            (trace, _noon, 8d, JobRunStatus.Failed, "2 rows failed", trace, true),
            (run.RunId, run.StartedAt, run.DurationSeconds, run.Status, run.Message, run.TraceId, run.TraceKept));
        Assert.Equal((1200L, 1250L, 2), (run.RecordsProcessed, run.RecordsTotal, run.Retries));
        Assert.Equal(
            [
                new JobStep("extract", 4, JobRunStatus.Succeeded, null),
                new JobStep("load", 2, JobRunStatus.Failed, "Deadlock victim"),
            ],
            run.Steps);
    }

    [Fact]
    public async Task WriteAsync_PipelineRunId_IsTheRunsIdAndTheResentRootStaysOneRun()
    {
        await using var database = await CreateAsync();
        var spans = Spans(database);
        var root = Span(Trace(1), Id(1), null, "deploy", _noon, 60_000) with
        {
            Attributes = new Dictionary<string, string>
            {
                ["cicd.pipeline.name"] = "deploy-web",
                ["cicd.pipeline.run.id"] = "4711",
            },
        };

        await spans.WriteAsync([root], default);
        await spans.WriteAsync([root], default);

        var job = _import with { Name = "deploy-web" };
        var run = Assert.Single(await Store(database).ListRunsAsync(job, _noon, _later, false, 10, default));
        Assert.Equal(("4711", JobRunStatus.Succeeded, 60d), (run.RunId, run.Status, run.DurationSeconds));
        Assert.Equal((null, null, null), (run.RecordsProcessed, run.RecordsTotal, run.Retries));
    }

    [Fact]
    public async Task WriteAsync_RootWithoutJobNameOrChildWithOne_KeepsNoRun()
    {
        await using var database = await CreateAsync();

        await Spans(database).WriteAsync(
            [
                Span(Trace(1), Id(1), null, "GET /invoices", _noon, 20),
                Span(Trace(2), Id(2), Id(1), "child", _noon, 20) with
                {
                    Attributes = new Dictionary<string, string> { ["job.name"] = "not a root" },
                },
            ],
            default);

        Assert.Empty(await Store(database).SummarizeAsync(new JobQuery(_noon.AddHours(-1), _later), default));
    }

    [Fact]
    public async Task WriteAsync_NameAttributesSetting_RecognisesTheInstallationsOwnAttribute()
    {
        await using var database = await CreateAsync();
        var options = new StorageOptions { Jobs = { NameAttributes = " quartz.job , job.name" } };
        var spans = new SpanStore(database, new TestOptionsMonitor<StorageOptions>(options));

        await spans.WriteAsync(
            [
                Span(Trace(1), Id(1), null, "run", _noon, 1_000) with
                {
                    Attributes = new Dictionary<string, string> { ["quartz.job"] = "purge" },
                },
                Span(Trace(2), Id(2), null, "run", _noon, 1_000) with
                {
                    Attributes = new Dictionary<string, string> { ["cicd.pipeline.name"] = "not listed" },
                },
            ],
            default);

        var jobs = await Store(database).SummarizeAsync(new JobQuery(_noon.AddHours(-1), _later), default);
        Assert.Equal(["purge"], jobs.Select(job => job.Job.Name));
    }

    [Fact]
    public async Task WriteAsync_AgentRunRecord_KeepsASqlAgentRunAndNotItsSteps()
    {
        await using var database = await CreateAsync();

        await new ActivityStore(database).WriteAsync(
            [
                Agent("101", "Nightly backup", "0", "0", 42, _noon, "The job failed."),
                Agent("100", "Nightly backup", "1", "0", 41, _noon, "Step failed."),
                Agent("102", "Nightly backup", "0", "1", 30, _noon.AddHours(-1), "The job succeeded."),
                Agent("103", "Nightly backup", "0", "3", 5, _noon.AddHours(-2), "Cancelled."),
            ],
            default);

        var runs = await Store(database).ListRunsAsync(_backup, _noon.AddHours(-3), _later, false, 10, default);

        Assert.Equal(
            [
                ("101", JobRunStatus.Failed, 42d, "The job failed."),
                ("102", JobRunStatus.Succeeded, 30d, "The job succeeded."),
                ("103", JobRunStatus.Failed, 5d, "Cancelled."),
            ],
            runs.Select(run => (run.RunId, run.Status, run.DurationSeconds!.Value, run.Message)));
        Assert.All(runs, run => Assert.Equal((null, false), (run.TraceId, run.TraceKept)));
        Assert.All(runs, run => Assert.Empty(run.Steps));
    }

    [Fact]
    public async Task SummarizeAsync_RunsOfAWindow_AddsUpEachJob()
    {
        await using var database = await CreateAsync();
        var spans = Spans(database);

        // 25 runs a minute apart; every fifth fails; durations of 10 to 34 seconds.
        await spans.WriteAsync(
            [
                .. Enumerable.Range(0, 25).Select(i => JobRoot(
                    i + 1,
                    "nightly-import",
                    _noon.AddMinutes(i - 30),
                    (10 + i) * 1000,
                    i % 5 == 4 ? SpanStatus.Error : SpanStatus.Ok,
                    processed: 100)),
                JobRoot(99, "other", _noon.AddMinutes(-1), 1000, SpanStatus.Ok) with { Environment = "staging" },
            ],
            default);

        var jobs = await Store(database).SummarizeAsync(new JobQuery(_noon.AddHours(-1), _noon), default);

        var import = jobs.Single(job => job.Job == _import);
        Assert.Equal((25, 20, 5), (import.Runs, import.Succeeded, import.Failed));
        Assert.Equal(
            (_noon.AddMinutes(-6), 34d, JobRunStatus.Failed),
            (import.LastRun.StartedAt, import.LastRun.DurationSeconds, import.LastRun.Status));
        Assert.Equal(JobSummary.RecentRuns, import.Recent.Count);
        Assert.Equal(JobRunStatus.Failed, import.Recent[^1]);
        Assert.Equal(JobRunStatus.Failed, import.Recent[^6]);
        Assert.Equal(JobRunStatus.Succeeded, import.Recent[0]);
        Assert.Equal(21.5, import.UsualSeconds);
        Assert.Equal(22, import.MedianSeconds);
        Assert.Equal(2_500L, import.RecordsProcessed);
        Assert.NotNull(import.P95Seconds);
        Assert.Equal(["nightly-import", "other"], jobs.Select(job => job.Job.Name).Order());
    }

    [Fact]
    public async Task SummarizeAsync_Filters_KeepTheEnvironmentAndUnnamedOnesTheSourceTheSearchAndTheJob()
    {
        await using var database = await CreateAsync();
        await Spans(database).WriteAsync(
            [
                JobRoot(1, "nightly-import", _noon.AddMinutes(-5), 1000, SpanStatus.Ok),
                JobRoot(2, "purge", _noon.AddMinutes(-5), 1000, SpanStatus.Ok) with { Environment = "staging" },
            ],
            default);
        await new ActivityStore(database).WriteAsync(
            [Agent("1", "Nightly backup", "0", "1", 3, _noon.AddMinutes(-5), "")],
            default);
        var store = Store(database);
        var window = new JobQuery(_noon.AddHours(-1), _noon);

        async Task<string[]> NamesAsync(JobQuery query) =>
            [.. (await store.SummarizeAsync(query, default)).Select(job => job.Job.Name).Order()];

        Assert.Equal(
            ["Nightly backup", "nightly-import"],
            await NamesAsync(window with { Environment = "production" }));
        Assert.Equal(["Nightly backup"], await NamesAsync(window with { Source = JobSource.SqlAgent }));
        Assert.Equal(["Nightly backup", "nightly-import"], await NamesAsync(window with { Search = "NIGHTLY" }));
        Assert.Equal(["Nightly backup"], await NamesAsync(window with { Search = "PROD-01" }));
        Assert.Equal(["purge"], await NamesAsync(window with { Search = "URG" }));
        Assert.Empty(await NamesAsync(window with { Search = "%" }));
        Assert.Equal(["Nightly backup"], await NamesAsync(window with { Job = _backup }));
        Assert.Empty(await NamesAsync(window with { From = _noon.AddMinutes(-4) }));
    }

    [Fact]
    public async Task ListRunsAsync_ExpiredTraceAndFailedOnly_SayWhatIsKeptAndKeepTheFailures()
    {
        await using var database = await CreateAsync();
        await Spans(database).WriteAsync(
            [
                JobRoot(1, "nightly-import", _noon.AddMinutes(-3), 1000, SpanStatus.Ok),
                JobRoot(2, "nightly-import", _noon.AddMinutes(-2), 1000, SpanStatus.Error),
                JobRoot(3, "nightly-import", _noon.AddMinutes(-1), 1000, SpanStatus.Ok),
            ],
            default);
        await using (var expire = database.CreateCommand("DELETE FROM storage.span WHERE trace_id = $1"))
        {
            expire.Parameters.AddWithValue(Convert.FromHexString(Trace(1)));
            await expire.ExecuteNonQueryAsync();
        }

        var store = Store(database);
        var all = await store.ListRunsAsync(_import, _noon.AddHours(-1), _noon, false, 10, default);
        var failed = await store.ListRunsAsync(_import, _noon.AddHours(-1), _noon, true, 10, default);
        var durations = await store.ListDurationsAsync(_import, _noon.AddHours(-1), _noon, 2, default);

        Assert.Equal(
            [(Trace(3), true), (Trace(2), true), (Trace(1), false)],
            all.Select(run => (run.RunId, run.TraceKept)));
        Assert.Equal([Trace(2)], failed.Select(run => run.RunId));
        Assert.Equal(
            [
                new JobDuration(_noon.AddMinutes(-1), 1, JobRunStatus.Succeeded),
                new JobDuration(_noon.AddMinutes(-2), 1, JobRunStatus.Failed),
            ],
            durations);
    }

    [Fact]
    public async Task ApplyAsync_RunsOlderThanTheRetention_AreDropped()
    {
        await using var database = await CreateAsync();
        var partitions = new JobRunPartitions(database, TimeProvider.System, NullLogger<JobRunPartitions>.Instance);
        await partitions.EnsureAsync(_noon.AddDays(-40), default);
        await Spans(database).WriteAsync(
            [
                JobRoot(1, "nightly-import", _noon.AddDays(-40), 1000, SpanStatus.Ok),
                JobRoot(2, "nightly-import", _noon.AddDays(-90), 1000, SpanStatus.Ok),
                JobRoot(3, "nightly-import", _noon.AddMinutes(-1), 1000, SpanStatus.Ok),
            ],
            default);
        var options = new TestOptionsMonitor<StorageOptions>(new StorageOptions());
        var retention = new JobRunRetention(
            database,
            options,
            TimeProvider.System,
            NullLogger<JobRunRetention>.Instance);

        var result = await retention.ApplyAsync(_noon, default);

        Assert.Equal(
            ["job_run_20261016", "job_run_20261017", "job_run_20261018", "job_run_20261019"],
            result.DroppedPartitions);
        Assert.Equal(1, result.DeletedDefaultRows);
        var left = await Store(database).ListRunsAsync(_import, _noon.AddDays(-100), _noon, false, 10, default);
        Assert.Equal([Trace(3)], left.Select(run => run.RunId));
    }

    internal static string Trace(int number) => number.ToString("x32", CultureInfo.InvariantCulture);

    internal static string Id(int number) => number.ToString("x16", CultureInfo.InvariantCulture);

    internal static SpanStore Spans(NpgsqlDataSource database) =>
        TestStores.Spans(database);

    internal static JobRunStore Store(NpgsqlDataSource database) => new(database);

    internal static Span JobRoot(
        int number,
        string job,
        DateTimeOffset start,
        int milliseconds,
        SpanStatus status,
        long? processed = null)
    {
        var attributes = new Dictionary<string, string> { ["job.name"] = job };

        if (processed is { } records)
        {
            attributes["records.processed"] = records.ToString(CultureInfo.InvariantCulture);
        }

        return Span(Trace(number), Id(number), null, "run " + job, start, milliseconds, status) with
        {
            Attributes = attributes,
        };
    }

    internal static Span Span(
        string traceId,
        string spanId,
        string? parentId,
        string name,
        DateTimeOffset start,
        int milliseconds,
        SpanStatus status = SpanStatus.Unset) => new(
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
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string> { ["service.name"] = "billing" },
        [],
        [],
        SpanDropped.None);

    internal static ActivityRecord Agent(
        string key,
        string job,
        string step,
        string status,
        int seconds,
        DateTimeOffset at,
        string message) => new(
        _server,
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

    private async Task<NpgsqlDataSource> CreateAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        await new SpanPartitions(database, TimeProvider.System, NullLogger<SpanPartitions>.Instance)
            .EnsureAsync(_noon, default);
        await new ActivityPartitions(database, TimeProvider.System, NullLogger<ActivityPartitions>.Instance)
            .EnsureAsync(_noon, default);
        await new JobRunPartitions(database, TimeProvider.System, NullLogger<JobRunPartitions>.Instance)
            .EnsureAsync(_noon, default);

        return database;
    }
}
