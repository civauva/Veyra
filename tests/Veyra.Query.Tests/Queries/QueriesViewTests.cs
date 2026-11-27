using Microsoft.Extensions.Time.Testing;
using Veyra.Core.Activity;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Queries;
using Veyra.Query.Queries.Api;
using Veyra.Query.Tests.Metrics;

namespace Veyra.Query.Tests.Queries;

/// <summary>
/// The Queries tab marks the server's own statements (VEY-64), so the screen can hide them unless asked, and lists them
/// apart, so its housekeeping never pushes the applications' statements out of the list.
/// </summary>
public class QueriesViewTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 4, 20, TimeSpan.Zero);
    private static readonly DateTimeOffset _baseline = new(2026, 11, 10, 11, 59, 0, TimeSpan.Zero);
    private static readonly TelemetryBlock _server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

    private readonly FakeMetricSearch _store = new();

    [Fact]
    public async Task ListAsync_SystemStatements_AreMarked()
    {
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", seconds: 2);
        AddStatement(
            "0xB2",
            "master",
            "SELECT SCHEMA_NAME(tbl.schema_id) AS [Schema] FROM sys.tables AS tbl",
            seconds: 5);
        AddStatement("0xC3", "shop", "delete top(@batch_size) from sys.[change_tracking_2089058478]", seconds: 3);

        var tab = await ListAsync(new QueryListing(System: true));

        Assert.Equal(
            [("0xB2", true), ("0xC3", true), ("0xA1", false)],
            tab.Statements.Select(statement => (statement.QueryHash, statement.System)));
    }

    [Fact]
    public async Task ListAsync_ManyBusierSystemStatements_LeaveTheApplicationsInTheList()
    {
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", seconds: 0.5);

        for (var index = 0; index < 120; index++)
        {
            AddStatement($"0xD{index}", "master", $"SELECT name FROM sys.objects WHERE id = {index}", seconds: 9);
        }

        var tab = await ListAsync(new QueryListing(System: true));

        Assert.Equal(100, tab.Statements.Count(statement => statement.System));
        Assert.Contains(tab.Statements, statement => statement.QueryHash == "0xA1" && !statement.System);
    }

    [Fact]
    public async Task ListAsync_SystemHidden_ListsOnlyTheApplicationsAndCountsTheRest()
    {
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders WHERE CustomerId = @id", seconds: 2);
        AddStatement("0xB2", "master", "SELECT name FROM sys.tables", seconds: 5);
        AddStatement("0xC3", "shop", "delete top(@batch_size) from sys.[change_tracking_2089058478]", seconds: 3);

        var tab = await ListAsync(new QueryListing(System: false));

        Assert.Equal(["0xA1"], tab.Statements.Select(statement => statement.QueryHash));
        Assert.Equal(2, tab.HiddenStatements.Count);
        Assert.Equal(["master"], tab.HiddenStatements.Names);
    }

    [Theory]
    [InlineData(QuerySort.Reads)]
    [InlineData(QuerySort.Runs)]
    [InlineData(QuerySort.TimePerRun)]
    public async Task ListAsync_RankedByAnotherMeasure_FindsTheTopStatementOutsideTheSlowest(QuerySort sort)
    {
        // 120 statements take more time in all than the one that reads, runs, and takes per run the most: ranked by
        // total time it would not be among the 100 sent, so it must be ranked on the server (VEY-66).
        for (var index = 0; index < 120; index++)
        {
            AddStatement(
                $"0xD{index}",
                "shop",
                $"SELECT Id FROM dbo.Orders WHERE Id = {index}",
                seconds: 9,
                runs: 100);
        }

        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Audit", seconds: 1, runs: 2_000, reads: 9e6);
        AddStatement("0xA2", "shop", "SELECT * FROM dbo.Report", seconds: 5);

        var tab = await ListAsync(new QueryListing(System: false, sort));

        Assert.Equal(100, tab.Statements.Count);
        Assert.Equal(sort == QuerySort.TimePerRun ? "0xA2" : "0xA1", tab.Statements[0].QueryHash);
    }

    [Fact]
    public async Task ListAsync_RankedByCpu_PutsTheBusiestFirst()
    {
        AddStatement("0xA1", "shop", "SELECT * FROM dbo.Orders", seconds: 4, cpu: 0.5);
        AddStatement("0xA2", "shop", "SELECT * FROM dbo.Carts", seconds: 2, cpu: 1.5);

        var tab = await ListAsync(new QueryListing(System: false, QuerySort.Cpu));

        Assert.Equal(["0xA2", "0xA1"], tab.Statements.Select(statement => statement.QueryHash));
    }

    [Fact]
    public async Task ListAsync_Simple_FindsAStatementWithAFindingOutsideTheBusiest()
    {
        // 120 busier statements show no problem; the one whose newest plan is ten times slower takes less time in all,
        // so it is not among the 100 busiest, and the Simple view must keep it on the server (VEY-67).
        for (var index = 0; index < 120; index++)
        {
            AddStatement(
                $"0xD{index}",
                "shop",
                $"SELECT Id FROM dbo.Orders WHERE Id = {index}",
                seconds: 9,
                runs: 100);
        }

        AddStatement(
            "0xR1",
            "shop",
            "SELECT * FROM dbo.Slow",
            seconds: 0.5,
            runs: 10,
            plan: "0xPold",
            seen: _now.AddMinutes(-30));
        AddStatement("0xR1", "shop", "SELECT * FROM dbo.Slow", seconds: 5, runs: 10, plan: "0xPnew");

        var advanced = await ListAsync(new QueryListing(System: false));
        var simple = await ListAsync(new QueryListing(System: false, Simple: true));

        Assert.DoesNotContain(advanced.Statements, statement => statement.QueryHash == "0xR1");
        var found = Assert.Single(simple.Statements);
        Assert.Equal(("0xR1", "regression"), (found.QueryHash, found.Findings[0].Kind));
        Assert.StartsWith("0xD", simple.Busiest!.QueryHash, StringComparison.Ordinal);
    }

    private Task<QueriesResponse> ListAsync(QueryListing listing) =>
        Builder().ListAsync("microsoft.sql_server", "sql-prod-01", "1h", listing, default);

    private QueriesViewBuilder Builder() => new(_store, new NoActivity(), new FakeTimeProvider(_now));

    // A statement as the agent sends it: one minute of the given time, with the given runs, CPU, and reads.
    private void AddStatement(
        string hash,
        string database,
        string text,
        double seconds,
        double runs = 1,
        double reads = 10,
        double? cpu = null,
        string? plan = null,
        DateTimeOffset? seen = null)
    {
        var attributes = new Dictionary<string, string>
        {
            ["query_hash"] = hash,
            ["database_name"] = database,
            ["query_text"] = text,
            ["agent"] = "false",
            ["plan_hash"] = plan ?? "0xP" + hash,
        };

        foreach (var (name, value) in new[]
        {
            ("sqlserver.statement.elapsed", seconds),
            ("sqlserver.statement.cpu", cpu ?? seconds / 2),
            ("sqlserver.statement.executions", runs),
            ("sqlserver.statement.logical_reads", reads),
        })
        {
            var series = _store.Add(new MetricSeries(
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
                },
                attributes,
                _now.AddDays(-1),
                seen ?? _now,
                _server));

            _store.Point(series, _baseline.AddMinutes(1), value);
        }
    }

    // The activity store, with nothing in it: no plans and no Query Store rows.
    private sealed class NoActivity : IDatabaseActivitySearch
    {
        public Task<SessionSnapshot?> GetSessionsAsync(
            string block,
            DateTimeOffset at,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            Task.FromResult<SessionSnapshot?>(null);

        public Task<IReadOnlyList<SessionSnapshot>> ListLatestSessionsAsync(
            DateTimeOffset now,
            TimeSpan maxAge,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSnapshot>>([]);

        public Task<IReadOnlyList<ActivityRecord>> ListRecordsAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            IReadOnlyCollection<string>? keys,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityRecord>>([]);

        public Task<IReadOnlyList<ActivityRecord>> ListRecordsMatchingAsync(
            string block,
            string kind,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            ActivityAttributeMatch match,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ActivityRecord>>([]);

        public Task<IReadOnlyList<SessionSample>> ListBlockingAsync(
            string block,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionSample>>([]);

        public Task<IReadOnlyList<SessionCaller>> ListCallersAsync(
            string block,
            string queryHash,
            DateTimeOffset periodStart,
            DateTimeOffset periodEnd,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionCaller>>([]);
    }
}
