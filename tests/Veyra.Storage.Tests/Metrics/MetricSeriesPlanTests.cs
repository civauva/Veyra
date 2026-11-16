using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Storage.Metrics;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Metrics;

/// <summary>
/// A database or container screen finds its series by block (ADR 0018) within the budgets of ADR 0015: through the
/// block index, never by reading every series.
/// </summary>
[Collection(PostgresGroup.Name)]
public class MetricSeriesPlanTests(PostgresFixture postgres)
{
    // As on a real installation, the table is too large to read whole, so PostgreSQL picks an index.
    private static readonly (string, string)[] _largeTable = [("enable_seqscan", "off")];

    [Fact]
    public async Task CreateFindCommand_Block_ReadsTheBlockIndex()
    {
        await using var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var query = new MetricSeriesQuery(
            DateTimeOffset.UnixEpoch,
            Environment: "production",
            Block: TelemetryBlock.Database("microsoft.sql_server", "sql-prod-01"));

        await using var command = MetricStore.CreateFindCommand(query);
        var plan = await QueryPlan.ExplainAsync(database, command, _largeTable);

        Assert.Contains("metric_series_block", plan.Indexes);
    }
}
