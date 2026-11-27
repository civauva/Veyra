using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;
using Veyra.Core.Traces;
using Veyra.Storage.Jobs;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Jobs;

/// <summary>
/// The job run reads meet their budgets by design (ADR 0015, ADR 0027): a window of every job reads the time index of
/// its days only, and one job's runs are read through the key index, newest first. These tests read the plans
/// PostgreSQL makes, so a change that loses an index or a partition bound fails here, not in production.
/// </summary>
[Collection(PostgresGroup.Name)]
public class JobRunQueryPlanTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _today = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    // As on a real installation, the table is too large to read whole, so PostgreSQL picks indexes.
    private static readonly (string, string)[] _largeTables = [("enable_seqscan", "off")];

    private static readonly JobKey _import = new(
        JobSource.Trace,
        "production",
        new TelemetryBlock(AlertBlockKind.Service, "billing"),
        "import-3");

    [Fact]
    public async Task CreateSummaryCommand_LastHour_ReadsOnlyTodaysTimeIndex()
    {
        await using var database = await CreateWeekAsync();

        await using var command = JobRunStore.CreateSummaryCommand(
            new JobQuery(_today.AddHours(11), _today.AddHours(12), Environment: "production", Search: "import"));
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(["job_run_20261126"], plan.Relations.Distinct());
        Assert.Equal(["job_run_20261126_started_at_idx"], plan.Indexes.Distinct());
    }

    [Fact]
    public async Task CreateSummaryCommand_OneJobOverAWeek_ReadsTheKeyIndexOfEachDay()
    {
        await using var database = await CreateWeekAsync();

        await using var command = JobRunStore.CreateSummaryCommand(
            new JobQuery(_today.AddDays(-6), _today.AddDays(1), Job: _import));
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        Assert.Equal(7, plan.Relations.Distinct().Count());
        Assert.DoesNotContain(plan.Relations, relation => relation.EndsWith("_default", StringComparison.Ordinal));
        Assert.NotEmpty(plan.Indexes);
        Assert.All(plan.Indexes, index => Assert.Contains("_source_environment_", index, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateRunsCommand_OneJob_ReadsTheKeyIndexAndTheTraceIndexForTheTraceLink()
    {
        await using var database = await CreateWeekAsync();

        await using var command = JobRunStore.CreateRunsCommand(
            _import,
            _today.AddDays(-6),
            _today.AddDays(1),
            failedOnly: false,
            limit: 200);
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTables);

        var runs = plan.Indexes.Where(index => index.StartsWith("job_run_", StringComparison.Ordinal)).ToList();
        var spans = plan.Indexes.Where(index => index.StartsWith("span_", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(runs);
        Assert.All(runs, index => Assert.Contains("_source_environment_", index, StringComparison.Ordinal));
        Assert.NotEmpty(spans);
        Assert.All(spans, index => Assert.Contains("trace_id", index, StringComparison.Ordinal));
    }

    /// <summary>A week of daily partitions, five jobs running every ten minutes, analysed.</summary>
    private async Task<NpgsqlDataSource> CreateWeekAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var jobs = new JobRunPartitions(database, TimeProvider.System, NullLogger<JobRunPartitions>.Instance);
        var spanPartitions = new SpanPartitions(database, TimeProvider.System, NullLogger<SpanPartitions>.Instance);

        for (var day = -7; day <= 1; day++)
        {
            await jobs.EnsureAsync(_today.AddDays(day), default);
            await spanPartitions.EnsureAsync(_today.AddDays(day), default);
        }

        var spans = new List<Span>();

        for (var i = 0; i < 7 * 24 * 6; i++)
        {
            for (var job = 0; job < 5; job++)
            {
                spans.Add(JobRunStoreTests.JobRoot(
                    (i * 5) + job + 1,
                    $"import-{job}",
                    _today.AddDays(-6).AddMinutes(i * 10),
                    30_000,
                    i % 13 == 0 ? SpanStatus.Error : SpanStatus.Ok));
            }
        }

        await JobRunStoreTests.Spans(database).WriteAsync(spans, default);

        await using var analyze = database.CreateCommand("ANALYZE storage.job_run, storage.span");
        await analyze.ExecuteNonQueryAsync();

        return database;
    }
}
