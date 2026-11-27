using Npgsql;
using Veyra.Core.Maintenance;
using Veyra.Storage.Metrics;

namespace Veyra.Storage.Maintenance;

/// <summary>
/// Deletes every log, metric, span, SQL Server activity record, and job run Veyra keeps, and their counts (VEY-59).
/// Truncating the partitioned parents empties every daily partition at once; the partitions themselves stay, so writes
/// carry on.
/// </summary>
/// <param name="dataSource">The database.</param>
/// <param name="metrics">The metric store, whose cached series IDs point at the series deleted here.</param>
internal sealed class StorageClearer(NpgsqlDataSource dataSource, MetricStore metrics) : ITelemetryClearer
{
    // The bookkeeping of the rollups and the one-time backfills stays: with no data left it has nothing to redo.
    private const string _sql = """
        TRUNCATE storage.log_record,
                 storage.log_count_minute,
                 storage.log_count_hour,
                 storage.metric_point,
                 storage.metric_series,
                 storage.metric_rollup_5m,
                 storage.metric_rollup_1h,
                 storage.metric_rollup_dirty,
                 storage.span,
                 storage.db_session,
                 storage.db_record,
                 storage.ingest_count_minute,
                 storage.job_run
        """;

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        metrics.ClearSeriesCache();

        await using (var command = dataSource.CreateCommand(_sql))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Again, for a write that resolved a series between the first clear and the truncate.
        metrics.ClearSeriesCache();
    }
}
