using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Telemetry;
using Veyra.Storage.Ingest;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Ingest;

/// <summary>
/// The Overview's ingestion must read only the days of its window from the minute counts (ADR 0015, ADR 0023). These
/// tests read the plan PostgreSQL makes, so a change that reads every day or the default partition fails here.
/// </summary>
[Collection(PostgresGroup.Name)]
public class IngestCountQueryPlanTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _today = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateCommand_SevenDaysByHour_ReadsOnlyThePartitionsOfThoseDays()
    {
        await using var database = await CreateMonthAsync();
        await using var command = IngestCounts.CreateCommand(
            "production",
            _today.AddDays(-7),
            _today,
            TimeSpan.FromHours(3));

        var plan = await QueryPlan.ExplainAsync(database, command);

        Assert.All(plan.Relations, relation => Assert.StartsWith("ingest_count_minute_202611", relation));
        Assert.Equal(7, plan.Relations.Distinct().Count());
    }

    [Fact]
    public async Task CreateCommand_LastHour_ReadsTodayOnlyByItsKey()
    {
        await using var database = await CreateMonthAsync();
        var now = _today.AddHours(15).AddSeconds(17);
        await using var command = IngestCounts.CreateCommand(null, now.AddHours(-1), now, TimeSpan.FromMinutes(1));

        var plan = await QueryPlan.ExplainAsync(database, command, [("enable_seqscan", "off")]);

        Assert.Equal(["ingest_count_minute_20261126"], plan.Relations.Distinct());
        Assert.All(plan.Indexes, index => Assert.StartsWith("ingest_count_minute_20261126", index));
        Assert.NotEmpty(plan.Indexes);
    }

    /// <summary>A month of daily partitions with a count a minute, analysed so the planner sees real tables.</summary>
    private async Task<NpgsqlDataSource> CreateMonthAsync()
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new IngestCountPartitions(
            database,
            TimeProvider.System,
            NullLogger<IngestCountPartitions>.Instance);

        for (var day = -30; day <= 0; day++)
        {
            await partitions.EnsureAsync(_today.AddDays(day), default);
        }

        var minutes = Enumerable.Range(0, 31 * 1440)
            .Select(minute => (_today.AddDays(-30).AddMinutes(minute), (string?)"production"));

        await using (var connection = await database.OpenConnectionAsync())
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await IngestCounts.AddAsync(connection, IngestSignal.Logs, minutes, default);
            await transaction.CommitAsync();
        }

        await using var analyze = database.CreateCommand("ANALYZE storage.ingest_count_minute");
        await analyze.ExecuteNonQueryAsync();

        return database;
    }
}
