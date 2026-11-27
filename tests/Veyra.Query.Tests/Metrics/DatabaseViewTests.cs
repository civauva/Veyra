using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Tests.Metrics;

public class DatabaseViewTests
{
    // 12:04:20 UTC: the last five one-minute buckets are 12:00 to 12:04, with 11:59 as their baseline.
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _baseline = new(2026, 11, 10, 11, 59, 0, TimeSpan.Zero);

    private static readonly TelemetryBlock _server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

    private readonly FakeMetricSearch _store = new();
    private readonly FakeBlockEvents _events = new();

    [Fact]
    public async Task DatabaseAsync_SqlServer_HasTheHeadlineNumbers()
    {
        AddRunningTotal("sqlserver.batch.request.rate", perSecond: 1240);
        AddLevel("sqlserver.user.connection.count", 182);
        AddLevel("sqlserver.processes.blocked", 0);
        AddLevel("sqlserver.page.life_expectancy", 3412);
        AddLevel("sqlserver.cpu.count", 8);
        AddLevel("sqlserver.query.duration.max", 134, ("database_name", "shop"));
        AddLevel("sqlserver.query.duration.max", 0, ("database_name", "reporting"));

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        var card = view.Card;
        Assert.Equal(("microsoft.sql_server", "sql-prod-01", "production"), (card.Engine, card.Server, card.Environment));
        Assert.Equal((1240d, 182d, 0d, 3412d), (card.BatchRequests, card.Connections, card.Blocked, card.PageLifeSeconds));
        Assert.Equal((8, 134d), (card.Cpus, card.LongestQuerySeconds));
        Assert.Equal(1240, view.Requests.Points[^1].Value);
    }

    [Fact]
    public async Task DatabaseAsync_Deadlocks_CountTheLastFifteenMinutes()
    {
        // Two deadlocks a minute in each of the five minutes after the baseline. Over a connection the receiver sends
        // SQL Server's running total of "Number of Deadlocks/sec", not a rate.
        AddRunningTotal("sqlserver.deadlock.rate", perSecond: 2 / 60d);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(10, view.Card.Deadlocks);
    }

    [Fact]
    public async Task DatabaseAsync_DeadlocksBeforeTheWindow_AreNotCounted()
    {
        // Three deadlocks since SQL Server started, none of them lately: the running total stands still.
        AddLevel("sqlserver.deadlock.rate", 3);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(0, view.Card.Deadlocks);
    }

    [Fact]
    public async Task DatabaseAsync_Databases_HaveSizeBackupAgeAndLongestQuery()
    {
        AddLevel("sqlserver.database.size", 412e9, ("database_name", "shop"), ("file_type", "data"));
        AddLevel("sqlserver.database.size", 8e9, ("database_name", "shop"), ("file_type", "log"));
        AddLevel("sqlserver.database.size", 24e9, ("database_name", "tempdb"), ("file_type", "data"));
        AddLevel("sqlserver.database.backup.age", 31 * 3600, ("database_name", "shop"));
        AddLevel("sqlserver.query.duration.max", 12, ("database_name", "shop"));

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(["shop", "tempdb"], view.Databases.Select(database => database.Name));
        var shop = view.Databases[0];
        Assert.Equal((420e9, 8e9), (shop.SizeBytes, shop.LogBytes));
        Assert.Equal((31d * 3600, 12d), (shop.BackupAgeSeconds, shop.LongestQuerySeconds));
        Assert.Null(view.Databases[1].BackupAgeSeconds);
        Assert.Equal((2, 31d * 3600), (view.Card.Databases, view.Card.OldestBackupSeconds));

        // tempdb needs no backup; shop's is older than 26 hours (VEY-68).
        Assert.Equal(["shop"], view.Overview!.LateBackups);
    }

    [Fact]
    public async Task DatabaseAsync_Jobs_ShowTheLastRunAndWhetherItFailed()
    {
        var started = new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);
        AddLevel("sqlserver.agent.job.failed", 1, ("job_name", "Nightly full backup - shop"));
        AddLevel("sqlserver.agent.job.last_run", started.ToUnixTimeSeconds(), ("job_name", "Nightly full backup - shop"));
        AddLevel("sqlserver.agent.job.duration", 4324, ("job_name", "Nightly full backup - shop"));
        AddLevel("sqlserver.agent.job.failed", 0, ("job_name", "Purge sessions"));

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(["Nightly full backup - shop", "Purge sessions"], view.Jobs.Select(job => job.Name));
        Assert.Equal((true, started, 4324d), (view.Jobs[0].Failed, view.Jobs[0].LastRun, view.Jobs[0].DurationSeconds));
        Assert.False(view.Jobs[1].Failed);
        Assert.Equal(1, view.Card.FailedJobs);
        Assert.Equal("Nightly full backup - shop", Assert.Single(view.Overview!.FailedJobs).Name);
    }

    [Fact]
    public async Task DatabaseAsync_Running_IsTheLatestSampleOfEachLongQuery()
    {
        AddLevel("sqlserver.batch.request.rate", 10);
        _events.Add(Sample(_now.AddSeconds(-20), "117", 120, "UPDATE dbo.Orders SET Status = @status"));
        _events.Add(Sample(_now.AddSeconds(-5), "117", 134, "UPDATE dbo.Orders SET Status = @status", blockedBy: "73"));
        _events.Add(Sample(_now.AddSeconds(-5), "98", 2, "SELECT TOP (50) * FROM dbo.OrderLines"));
        _events.Add(Sample(_now.AddSeconds(-5), "203", 41, "SELECT c.Region FROM dbo.Orders"));

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(["117", "203"], view.Running.Select(query => query.Session));
        var longest = view.Running[0];
        Assert.Equal((134d, "shop", "app_shop", "LCK_M_U", "73"), (longest.ElapsedSeconds, longest.Database, longest.User, longest.Wait, longest.BlockedBy));
    }

    [Fact]
    public async Task DatabaseAsync_TopQueries_AddUpEveryMinuteOfTheRange()
    {
        AddLevel("sqlserver.batch.request.rate", 10);
        // What each minute added: the app's lookup ran 200 times in each of two minutes, the report once.
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", agent: false, [(6, 200), (6.5, 200)]);
        AddStatement("0xB2", "reporting", "EXEC dbo.MonthlyReport", agent: false, [(80, 1)]);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(
            ["EXEC dbo.MonthlyReport", "SELECT * FROM dbo.Orders WHERE CustomerId = @id"],
            view.TopQueries.Select(query => query.Text));
        var lookup = view.TopQueries[1];
        Assert.Equal((12.5d, 400L, "shop"), (lookup.TotalSeconds, lookup.Executions, lookup.Database));
        Assert.Equal((6.25d, 4000L), (lookup.WorkerSeconds, lookup.LogicalReads));
        Assert.Equal("reporting", view.TopQueries[0].Database);
    }

    [Fact]
    public async Task DatabaseAsync_AgentsOwnStatements_AreSetApartFromTheRanking()
    {
        AddLevel("sqlserver.batch.request.rate", 10);
        AddStatement("0xC3", "master", "WITH PerfCounters SELECT ...", agent: true, [(3, 4), (2, 4)]);
        AddStatement("0xC4", "master", "SELECT d.name, DATEDIFF_BIG(...)", agent: true, [(1, 1)]);
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", agent: false, [(0.2, 10)]);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(["SELECT * FROM dbo.Orders WHERE CustomerId = @id"], view.TopQueries.Select(query => query.Text));
        Assert.Equal(new AgentWorkResponse(2, 6), view.AgentWork);
    }

    [Fact]
    public async Task DatabaseAsync_SystemDatabasesAndJobs_AreMarked()
    {
        AddLevel("sqlserver.database.size", 412e9, ("database_name", "shop"), ("file_type", "data"));
        AddLevel("sqlserver.database.size", 2e9, ("database_name", "msdb"), ("file_type", "data"));
        AddLevel("sqlserver.agent.job.failed", 0, ("job_name", "syspolicy_purge_history"));
        AddLevel("sqlserver.agent.job.failed", 0, ("job_name", "Purge sessions"));

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(
            [("shop", false), ("msdb", true)],
            view.Databases.Select(database => (database.Name, database.System)));
        Assert.Equal(
            [("Purge sessions", false), ("syspolicy_purge_history", true)],
            view.Jobs.Select(job => (job.Name, job.System)));
    }

    [Fact]
    public async Task DatabaseAsync_SystemStatements_AreMarkedAndRankedApart()
    {
        AddLevel("sqlserver.batch.request.rate", 10);
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", agent: false, [(0.2, 10)]);

        // Twelve busier statements of SSMS browsing the catalog: they do not push the application's out of the list.
        for (var index = 0; index < 12; index++)
        {
            AddStatement(
                $"0xD{index}",
                "master",
                $"SELECT name FROM sys.objects WHERE id = {index}",
                agent: false,
                [(5, 1)]);
        }

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(11, view.TopQueries.Count);
        Assert.Equal(10, view.TopQueries.Count(query => query.System));
        Assert.False(view.TopQueries.Single(query => query.Database == "shop").System);
    }

    [Fact]
    public async Task DatabaseAsync_SystemHidden_ListsTheApplicationsAndCountsWhatItLeftOut()
    {
        AddLevel("sqlserver.database.size", 412e9, ("database_name", "shop"), ("file_type", "data"));
        AddLevel("sqlserver.database.size", 2e9, ("database_name", "msdb"), ("file_type", "data"));
        AddLevel("sqlserver.agent.job.failed", 0, ("job_name", "syspolicy_purge_history"));
        AddLevel("sqlserver.agent.job.failed", 0, ("job_name", "Purge sessions"));
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", agent: false, [(0.2, 10)]);

        for (var index = 0; index < 12; index++)
        {
            AddStatement(
                $"0xD{index}",
                "master",
                $"SELECT name FROM sys.objects WHERE id = {index}",
                agent: false,
                [(5, 1)]);
        }

        var view = (await Builder().DatabaseAsync(
            "microsoft.sql_server",
            "sql-prod-01",
            "1h",
            system: false,
            default))!;

        Assert.Equal(["shop"], view.Databases.Select(database => database.Name));
        Assert.Equal(["Purge sessions"], view.Jobs.Select(job => job.Name));
        Assert.Equal(["shop"], view.TopQueries.Select(query => query.Database));
        Assert.Equal(1, view.HiddenDatabases.Count);
        Assert.Equal(["msdb"], view.HiddenDatabases.Names);
        Assert.Equal(1, view.HiddenJobs.Count);
        Assert.Equal(12, view.HiddenTopQueries.Count);
        Assert.Equal(["master"], view.HiddenTopQueries.Names);
    }

    [Fact]
    public async Task DatabaseAsync_NoStatements_HasNoTopQueriesAndNoAgentWork()
    {
        AddLevel("sqlserver.batch.request.rate", 10);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Empty(view.TopQueries);
        Assert.Null(view.AgentWork);
    }

    [Fact]
    public async Task DatabaseAsync_Waits_LeaveIdleWaitsOut()
    {
        AddWaits("Lock", "LCK_M_U", perSecond: 0.2);
        AddWaits("OTHER", "SLEEP_TASK", perSecond: 0.9);
        AddWaits("OTHER", "WAITFOR", perSecond: 0.5);
        AddWaits("Buffer IO", "PAGEIOLATCH_SH", perSecond: 0.1);

        var view = (await Builder().DatabaseAsync("microsoft.sql_server", "sql-prod-01", "1h", system: true, default))!;

        Assert.Equal(["Lock", "Buffer IO"], view.Waits.Select(line => line.Label));
    }

    [Fact]
    public async Task DatabaseAsync_UnknownServer_IsNull()
    {
        AddLevel("sqlserver.batch.request.rate", 10);

        Assert.Null(await Builder().DatabaseAsync("microsoft.sql_server", "sql-other", "1h", system: true, default));
    }

    [Fact]
    public async Task OverviewAsync_DatabaseServer_HasACard()
    {
        AddRunningTotal("sqlserver.batch.request.rate", perSecond: 1240);
        AddLevel("sqlserver.processes.blocked", 3);

        var overview = await new MetricViewBuilder(_store, new FakeTimeProvider(_now)).OverviewAsync("1h", default);

        var card = Assert.Single(overview.Databases);
        Assert.Equal(("sql-prod-01", 1240d, 3d), (card.Server, card.BatchRequests, card.Blocked));
        Assert.Empty(overview.Hosts);
        Assert.Empty(overview.Services);
    }

    [Fact]
    public async Task OverviewAsync_SeveralEngines_ListsTheServersByName()
    {
        // By block name, microsoft.sql_server zeta would come before postgresql alpha (VEY-68).
        AddLevel("sqlserver.processes.blocked", 0);
        _store.Add(AddSeries("postgresql.backends", []) with
        {
            Id = 0,
            Block = TelemetryBlock.Database("postgresql", "alpha"),
            ResourceAttributes = new Dictionary<string, string>
            {
                ["db.system.name"] = "postgresql",
                ["server.address"] = "alpha",
            },
        });

        var overview = await new MetricViewBuilder(_store, new FakeTimeProvider(_now)).OverviewAsync("1h", default);

        Assert.Equal(["alpha", "sql-prod-01"], overview.Databases.Select(card => card.Server));
    }

    private DatabaseViewBuilder Builder() => new(_store, _events, new FakeTimeProvider(_now));

    // A statement as the agent sends it: what each minute added to its time and runs, with CPU half its time and ten
    // reads a run.
    private void AddStatement(
        string hash,
        string database,
        string text,
        bool agent,
        (double Seconds, long Runs)[] minutes)
    {
        var attributes = new (string Key, string Value)[]
        {
            ("query_hash", hash),
            ("database_name", database),
            ("query_text", text),
            ("agent", agent ? "true" : "false"),
        };
        var elapsed = AddSeries("sqlserver.statement.elapsed", attributes);
        var cpu = AddSeries("sqlserver.statement.cpu", attributes);
        var runs = AddSeries("sqlserver.statement.executions", attributes);
        var reads = AddSeries("sqlserver.statement.logical_reads", attributes);

        for (var minute = 0; minute < minutes.Length; minute++)
        {
            var at = _baseline.AddMinutes(minute + 1);
            _store.Point(elapsed, at, minutes[minute].Seconds);
            _store.Point(cpu, at, minutes[minute].Seconds / 2);
            _store.Point(runs, at, minutes[minute].Runs);
            _store.Point(reads, at, minutes[minute].Runs * 10);
        }
    }

    // A counter as SQL Server keeps it: a total since it started, here 50,000 at the baseline, growing at a steady rate.
    private void AddRunningTotal(string name, double perSecond)
    {
        var series = AddSeries(name);

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), 50_000 + (perSecond * 60 * minute));
        }
    }

    private void AddLevel(string name, double value, params (string Key, string Value)[] attributes)
    {
        var series = AddSeries(name, attributes);

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), value);
        }
    }

    private MetricSeries AddSeries(string name, params (string Key, string Value)[] attributes) =>
        _store.Add(new MetricSeries(
            0,
            name,
            MetricKind.Gauge,
            "1",
            string.Empty,
            MetricTemporality.Unspecified,
            false,
            null,
            "production",
            null,
            new Dictionary<string, string>
            {
                ["db.system.name"] = "microsoft.sql_server",
                ["server.address"] = "sql-prod-01",
                ["deployment.environment.name"] = "production",
            },
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            _now.AddDays(-1),
            _now,
            _server));

    private void AddWaits(string category, string type, double perSecond)
    {
        var series = _store.Add(new MetricSeries(
            0,
            "sqlserver.os.wait.duration",
            MetricKind.Sum,
            "s",
            string.Empty,
            MetricTemporality.Cumulative,
            true,
            null,
            "production",
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["wait.category"] = category, ["wait.type"] = type },
            _now.AddDays(-1),
            _now,
            _server));

        for (var minute = 0; minute <= 5; minute++)
        {
            _store.Point(series, _baseline.AddMinutes(minute), perSecond * 60 * minute);
        }
    }

    private static LogRecord Sample(
        DateTimeOffset at,
        string session,
        double elapsed,
        string text,
        string? blockedBy = null) => Event(at, "db.server.query_sample", new Dictionary<string, string>
        {
            ["db.namespace"] = "shop",
            ["user.name"] = "app_shop",
            ["db.query.text"] = text,
            ["sqlserver.session_id"] = session,
            ["sqlserver.total_elapsed_time"] = elapsed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sqlserver.wait_type"] = blockedBy is null ? "" : "LCK_M_U",
            ["sqlserver.blocking_session_id"] = blockedBy ?? "0",
            ["sqlserver.request_status"] = blockedBy is null ? "running" : "suspended",
        });

    private static LogRecord Event(DateTimeOffset at, string name, Dictionary<string, string> attributes)
    {
        attributes["event.name"] = name;

        return new LogRecord(
            at,
            at,
            LogSeverity.Information,
            null,
            "{}",
            _server.Name,
            "production",
            null,
            null,
            attributes,
            new Dictionary<string, string>(),
            AlertBlockKind.Database);
    }

    private sealed class FakeBlockEvents : IBlockEvents
    {
        private readonly List<LogRecord> _records = [];

        public void Add(LogRecord record) => _records.Add(record);

        public Task<IReadOnlyList<LogRecord>> ListAsync(
            TelemetryBlock block,
            string eventName,
            DateTimeOffset since,
            DateTimeOffset until,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LogRecord>>(_records
                .Where(record => record.ServiceName == block.Name && record.BlockKind == block.Kind)
                .Where(record => record.Attributes.GetValueOrDefault("event.name") == eventName)
                .Where(record => record.Timestamp >= since && record.Timestamp < until)
                .OrderByDescending(record => record.Timestamp)
                .Take(limit)
                .ToList());
    }
}
