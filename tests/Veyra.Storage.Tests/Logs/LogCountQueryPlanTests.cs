using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Logs;

/// <summary>
/// Counting must read the counts, and the records only at the edges of a window (ADR 0015). These tests read the plan
/// PostgreSQL makes, so a change that sends counting back to the records fails here, not in production.
/// </summary>
[Collection(PostgresGroup.Name)]
public class LogCountQueryPlanTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _today = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateCommand_SevenWholeDaysByHour_ReadsOnlyTheHourCountsOfThoseDays()
    {
        await using var database = await CreateWeekAsync();
        var query = Query(_today.AddDays(-7), _today);

        var plan = await ExplainAsync(database, query, TimeSpan.FromHours(1));

        Assert.All(plan.Relations, relation => Assert.StartsWith("log_count_hour_202611", relation));
        Assert.Equal(7, plan.Relations.Distinct().Count());
    }

    [Fact]
    public async Task CreateCommand_ExactLevels_StillReadOnlyTheHourCounts()
    {
        await using var database = await CreateWeekAsync();
        var query = Query(_today.AddDays(-7), _today) with { Levels = [LogSeverity.Error, LogSeverity.Fatal] };

        var plan = await ExplainAsync(database, query, TimeSpan.FromHours(1));

        Assert.All(plan.Relations, relation => Assert.StartsWith("log_count_hour_202611", relation));
    }

    [Fact]
    public async Task CreateCommand_UnalignedWindow_ReadsRecordsOnlyOfTheEdgeDaysByTime()
    {
        await using var database = await CreateWeekAsync();
        var query = Query(_today.AddDays(-7).AddSeconds(17), _today.AddHours(-1).AddSeconds(-5));

        var plan = await ExplainAsync(database, query, TimeSpan.FromHours(1));

        var records = plan.Relations.Where(relation => relation.StartsWith("log_record", StringComparison.Ordinal));
        Assert.Equal(["log_record_20261102", "log_record_20261108"], records.Distinct().Order());
        Assert.DoesNotContain(plan.Relations, relation => relation.EndsWith("_default", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCommand_LastFiveMinutes_ReadsMinuteCountsAndRecordsOfToday()
    {
        await using var database = await CreateWeekAsync();
        var now = _today.AddHours(-1).AddMinutes(-7).AddSeconds(-13);
        var query = Query(now.AddMinutes(-5), now);

        var plan = await ExplainAsync(database, query, bucketSize: null);

        Assert.Equal(
            ["log_count_minute_20261108", "log_record_20261108"],
            plan.Relations.Distinct().Order());
    }

    private static LogQuery Query(DateTimeOffset from, DateTimeOffset to) =>
        new(from, to, LogSeverity.Warning, null, null, null, 100, null, ["production"]);

    private static async Task<QueryPlan> ExplainAsync(NpgsqlDataSource database, LogQuery query, TimeSpan? bucketSize)
    {
        await using var command = LogCounts.CreateCommand(query, bucketSize, countable: true, uncounted: []);

        return await QueryPlan.ExplainAsync(database, command);
    }

    /// <summary>A week of daily partitions with a few records each, analysed so the planner sees real tables.</summary>
    [Fact]
    public async Task CreateEventsCommand_DatabaseServer_ReadsTheNameAndTimeIndexOfItsDaysOnly()
    {
        await using var database = await CreateWeekAsync();
        var server = TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01");

        await using var command = LogStore.CreateEventsCommand(
            server,
            "db.server.query_sample",
            _today.AddSeconds(-45),
            _today.AddSeconds(5),
            500);
        var plan = await QueryPlan.ExplainAsync(database, command, [("enable_seqscan", "off")]);

        Assert.Equal(["log_record_20261108", "log_record_20261109"], plan.Relations.Distinct().Order());
        Assert.All(plan.Indexes, index => Assert.Contains("service_name", index, StringComparison.Ordinal));
        Assert.NotEmpty(plan.Indexes);
    }

    private async Task<NpgsqlDataSource> CreateWeekAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new LogPartitions(database, TimeProvider.System, NullLogger<LogPartitions>.Instance);
        var store = new LogStore(database);

        for (var day = -8; day <= 0; day++)
        {
            await partitions.EnsureAsync(_today.AddDays(day), default);
        }

        for (var hour = -8 * 24; hour < 0; hour++)
        {
            var at = _today.AddHours(hour);

            await store.WriteAsync(
                [
                    Record(at.AddMinutes(1), LogSeverity.Information),
                    Record(at.AddMinutes(31), LogSeverity.Error),
                ],
                default);
        }

        await using var analyze = database.CreateCommand("ANALYZE");
        await analyze.ExecuteNonQueryAsync();

        return database;
    }

    private static LogRecord Record(DateTimeOffset timestamp, LogSeverity severity) => new(
        timestamp,
        timestamp,
        severity,
        null,
        "message",
        "payments",
        "production",
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());
}
