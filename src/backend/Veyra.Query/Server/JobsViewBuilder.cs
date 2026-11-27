using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Server.Api;
using Veyra.Query.Systems;

namespace Veyra.Query.Server;

/// <summary>Builds a SQL Server's Jobs tab (ADR 0020, area 10) from the agent's job metrics and history.</summary>
/// <param name="search">The metric series and points.</param>
/// <param name="activity">The job history records.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class JobsViewBuilder(IMetricSearch search, IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record a run or step of a job is.</summary>
    public const string JobKind = "sqlserver.job";

    private static readonly string[] _names =
    [
        SqlServerNames.JobFailed,
        SqlServerNames.JobLastRun,
        SqlServerNames.JobDuration,
        "sqlserver.agent.job.running",
        "sqlserver.agent.job.next_run",
        "sqlserver.agent.job.late",
        "sqlserver.agent.job.usual_duration",
    ];

    private readonly MetricReader _reader = new(search);

    /// <summary>Builds the tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="system">Whether the jobs SQL Server set up itself are listed (VEY-66).</param>
    /// <param name="simple">Whether only the jobs that need a look are listed (VEY-67).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The jobs, those that need a look first.</returns>
    public async Task<JobsResponse> JobsAsync(
        string engine,
        string server,
        bool system,
        bool simple,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var block = TelemetryBlock.Database(engine, server);
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(now.AddHours(-1), Names: _names, Limit: 5_000, Block: block),
            cancellationToken);
        var recentReading = _reader.ReadAsync(
            series,
            MetricWindow.Covering(now.AddMinutes(-15), now, 15),
            cancellationToken);
        var historyReading = activity.ListRecordsAsync(
            block.Name,
            JobKind,
            now.AddDays(-7),
            now,
            null,
            5_000,
            cancellationToken);

        await Task.WhenAll(recentReading, historyReading);

        var recent = await recentReading;
        Dictionary<string, double> Latest(string name) =>
            DatabaseView.LatestBy(recent, name, SqlServerNames.JobAttribute);

        var failed = Latest(SqlServerNames.JobFailed);
        var lastRun = Latest(SqlServerNames.JobLastRun);
        var duration = Latest(SqlServerNames.JobDuration);
        var running = Latest("sqlserver.agent.job.running");
        var next = Latest("sqlserver.agent.job.next_run");
        var late = Latest("sqlserver.agent.job.late");
        var usual = Latest("sqlserver.agent.job.usual_duration");
        var history = (await historyReading)
            .GroupBy(record => record.Key)
            .Select(group => group.First())
            .GroupBy(record => record.Attributes.GetValueOrDefault("job_name") ?? "")
            .ToDictionary(group => group.Key, group => group.ToList());

        var jobs = failed.Keys.Union(running.Keys).Union(next.Keys)
            .Select(job =>
            {
                double? Value(Dictionary<string, double> values) =>
                    values.TryGetValue(job, out var value) && value >= 0 ? value : null;

                var runs = (history.GetValueOrDefault(job) ?? [])
                    .Where(record => record.Attributes.GetValueOrDefault("step_id") == "0")
                    .OrderByDescending(record => record.SampledAt)
                    .Take(20)
                    .Select(run => new JobRunResponse(
                        run.SampledAt,
                        run.Attributes.GetValueOrDefault("run_status") == "1",
                        Number(run, "duration"),
                        run.Body,
                        (history.GetValueOrDefault(job) ?? [])
                            .Where(step => step.Attributes.GetValueOrDefault("step_id") != "0"
                                && Math.Abs((step.SampledAt - run.SampledAt).TotalSeconds)
                                    <= Number(run, "duration") + 1)
                            .OrderBy(step => Number(step, "step_id"))
                            .Select(step => new JobStepResponse(
                                (int)Number(step, "step_id"),
                                step.Attributes.GetValueOrDefault("step_name") ?? "",
                                step.Attributes.GetValueOrDefault("run_status") == "1",
                                Number(step, "duration"),
                                step.Body))
                            .ToList()))
                    .ToList();

                return new JobResponse(
                    job,
                    Value(failed) >= 1,
                    Value(lastRun) is { } started ? DateTimeOffset.FromUnixTimeSeconds((long)started) : null,
                    Value(duration),
                    Value(running),
                    Value(next) is { } at ? DateTimeOffset.FromUnixTimeSeconds((long)at) : null,
                    Value(late) >= 1,
                    Value(usual),
                    runs)
                {
                    System = SystemObjects.IsSystemJob(job),
                };
            })
            .ToList();

        return Keep(jobs, system, simple);
    }

    /// <summary>
    /// Orders the jobs, those that need a look first, and keeps those asked for. Pure; tested without a database.
    /// </summary>
    /// <param name="jobs">Every enabled job.</param>
    /// <param name="system">Whether the jobs SQL Server set up itself are listed (VEY-66).</param>
    /// <param name="simple">Whether only the jobs that need a look are listed, as in the Simple view (VEY-67).</param>
    /// <returns>The tab.</returns>
    public static JobsResponse Keep(IReadOnlyList<JobResponse> jobs, bool system, bool simple)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        var ordered = jobs
            .OrderByDescending(job => job.NeedsLook)
            .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var (listed, hidden) = SystemRows.Keep(ordered, system, job => job.System, job => job.Name);

        return new JobsResponse([.. listed.Where(job => !simple || job.NeedsLook)], hidden, listed.Count);
    }

    private static double Number(ActivityRecord record, string key) =>
        double.TryParse(
            record.Attributes.GetValueOrDefault(key),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
