using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Removes old metrics: whole daily partitions of points older than the retention, old rows of the default partition,
/// and series that had no point for longer than the retention (ADR 0010).
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="store">The metric store, whose series cache is cleared when series are deleted.</param>
/// <param name="options">The Storage settings with the retention.</param>
/// <param name="clock">The clock that decides what is old.</param>
/// <param name="logger">Receives what was removed.</param>
internal sealed partial class MetricRetention(
    NpgsqlDataSource dataSource,
    MetricStore store,
    IOptions<StorageOptions> options,
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
        var days = options.Value.MetricRetentionDays;
        var firstDayKept = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-days);
        var cutoff = new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dropped = new List<string>();

        var partitions = await DailyPartitions.ListAsync(dataSource, MetricPartitions.Table, cancellationToken);

        foreach (var partition in partitions)
        {
            if (partition.Day >= firstDayKept)
            {
                continue;
            }

            // The name comes from pg_class and was checked to be metric_point_ followed by a date: safe to inline.
            await using var drop = dataSource.CreateCommand($"DROP TABLE IF EXISTS storage.{partition.Name}");
            await drop.ExecuteNonQueryAsync(cancellationToken);

            dropped.Add(partition.Name);
            LogDropped(partition.Name, days);
        }

        await using (var command = dataSource.CreateCommand(
            "DELETE FROM storage.metric_point_default WHERE timestamp < $1"))
        {
            command.Parameters.AddWithValue(cutoff);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
