using Veyra.Core.Activity;
using Veyra.Query.Server;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Tests.Server;

/// <summary>The Simple view of a database server's Jobs and Events tabs is kept on the server (VEY-67).</summary>
public class SimpleViewTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Keep_Simple_ListsOnlyTheJobsThatNeedALookAndCountsEveryJob()
    {
        JobResponse[] jobs =
        [
            Job("backup") with { Failed = true },
            Job("cleanup"),
            Job("etl") with { RunningSeconds = 900, UsualSeconds = 300 },
            Job("index rebuild") with { RunningSeconds = 400, UsualSeconds = 300 },
            Job("report") with { Late = true },
        ];

        var simple = JobsViewBuilder.Keep(jobs, system: false, simple: true);
        var advanced = JobsViewBuilder.Keep(jobs, system: false, simple: false);

        Assert.Equal(["backup", "etl", "report"], simple.Jobs.Select(job => job.Name));
        Assert.Equal(5, simple.Enabled);
        Assert.Equal(["backup", "etl", "report", "cleanup", "index rebuild"], advanced.Jobs.Select(job => job.Name));
    }

    [Fact]
    public void Keep_Simple_LeavesTheServersOwnJobsOutOfTheCount()
    {
        JobResponse[] jobs = [Job("backup"), Job("syspolicy_purge_history") with { System = true }];

        var simple = JobsViewBuilder.Keep(jobs, system: false, simple: true);

        Assert.Empty(simple.Jobs);
        Assert.Equal((1, 1), (simple.Enabled, simple.HiddenJobs.Count));
    }

    [Fact]
    public void Shown_Simple_LeavesOutTheEventsThatNeedNothing()
    {
        ActivityRecord[] records =
        [
            Record("1", "connectivity_ring_buffer_recorded"),
            Record("2", "error_reported", ("error_number", "824"), ("severity", "24")),
            Record("3", "connectivity_ring_buffer_recorded"),
        ];

        var simple = EventsViewBuilder.Shown(records, simple: true);
        var advanced = EventsViewBuilder.Shown(records, simple: false);

        Assert.Equal("error_reported", Assert.Single(simple).Name);
        Assert.Equal(3, advanced.Count);
    }

    [Theory]
    [InlineData("error_reported")]
    [InlineData("wait_info")]
    [InlineData("wait_info_external")]
    [InlineData("scheduler_monitor_non_yielding_ring_buffer_recorded")]
    [InlineData("security_error_ring_buffer_recorded")]
    [InlineData("connectivity_ring_buffer_recorded")]
    [InlineData("sp_server_diagnostics_component_result")]
    public void NeedsSomething_AnEvent_MatchesExactlyWhenItsSeverityIsNotOk(string name)
    {
        var record = Record("1", name);

        var matches = EventsViewBuilder.NeedsSomething.Matches(record.Attributes);

        Assert.Equal(EventsViewBuilder.Event(record).Severity != "ok", matches);
    }

    private static JobResponse Job(string name) => new(name, false, _now.AddHours(-1), 60, null, null, false, 60, []);

    private static ActivityRecord Record(string key, string name, params (string Key, string Value)[] more) =>
        new(
            "microsoft.sql_server sql-prod-01",
            "production",
            _now,
            EventsViewBuilder.EventKind,
            key,
            new Dictionary<string, string>(more.Select(pair => KeyValuePair.Create(pair.Key, pair.Value)))
            {
                ["event_name"] = name,
            },
            "");
}
