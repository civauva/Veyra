using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Removes old metrics: whole daily partitions of points and their rollups older than the retention, old rows of the
/// default partitions, and series that had no point for longer than the retention (ADR 0010, ADR 0015).
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="store">The metric store, whose series cache is cleared when series are deleted.</param>
/// <param name="options">The Storage settings with the retention.</param>
/// <param name="clock">The clock that decides what is old.</param>
/// <param name="logger">Receives what was removed.</param>
internal sealed partial class MetricRetention(
    NpgsqlDataSource dataSource,
    MetricStore store,
    IOptionsMonitor<StorageOptions> options,
    TimeProvider clock,
    ILogger<MetricRetention> logger)
{
    /// <summary>Applies retention as of now.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The partitions dropped and the series deleted.</returns>
    public Task<(IReadOnlyList<string> Dropped, int SeriesDeleted)> ApplyAsync(CancellationToken cancellationToken) =>
        ApplyAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Applies retention as of the given time.</summary>
    /// <param name="now">The time to measure age from.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The partitions dropped and the series deleted.</returns>
    public async Task<(IReadOnlyList<string> Dropped, int SeriesDeleted)> ApplyAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var days = options.CurrentValue.MetricRetentionDays;
        var firstDayKept = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-days);
        var cutoff = new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dropped = new List<string>();

        foreach (var table in MetricPartitions.Tables)
        {
            foreach (var partition in await DailyPartitions.ListAsync(dataSource, table, cancellationToken))
            {
                if (partition.Day >= firstDayKept)
                {
                    continue;
                }

                // The name comes from pg_class and was checked to be the table, "_", and a date: safe to inline.
                await using var drop = dataSource.CreateCommand($"DROP TABLE IF EXISTS storage.{partition.Name}");
                await drop.ExecuteNonQueryAsync(cancellationToken);

                if (table == MetricPartitions.Table)
                {
                    dropped.Add(partition.Name);
                    LogDropped(partition.Name, days);
                }
            }
        }

        await using (var batch = dataSource.CreateBatch())
        {
            foreach (var (sql, value) in new (string, object)[]
            {
                ("DELETE FROM storage.metric_point_default WHERE timestamp < $1", cutoff),
                ("DELETE FROM storage.metric_rollup_5m_default WHERE bucket < $1", cutoff),
                ("DELETE FROM storage.metric_rollup_1h_default WHERE bucket < $1", cutoff),
                ("DELETE FROM storage.metric_rollup_dirty WHERE hour < $1", cutoff),
                ("DELETE FROM storage.metric_rollup_backfill WHERE day < $1", firstDayKept),
            })
            {
                var command = new NpgsqlBatchCommand(sql);
                command.Parameters.Add(new NpgsqlParameter { Value = value });
                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }

        int deleted;

        await using (var command = dataSource.CreateCommand(
            "DELETE FROM storage.metric_series WHERE last_seen < $1"))
        {
            command.Parameters.AddWithValue(cutoff);
            deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (deleted > 0)
        {
            store.ClearSeriesCache();
            LogSeriesDeleted(deleted, days);
        }

        return (dropped, deleted);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention dropped metric partition {Partition}, older than {Days} days")]
    private partial void LogDropped(string partition, int days);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention deleted {Count} metric series without points for {Days} days")]
    private partial void LogSeriesDeleted(int count, int days);
}
