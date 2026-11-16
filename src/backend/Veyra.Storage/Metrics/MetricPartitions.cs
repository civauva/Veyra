using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Creates the daily partitions of <c>storage.metric_point</c> ahead of time, from yesterday to two days ahead in UTC,
/// the same way as for logs. Points outside them land in the default partition and are kept.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">The clock that decides which days are needed.</param>
/// <param name="logger">Receives a warning for each day that could not get its own partition.</param>
internal sealed partial class MetricPartitions(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<MetricPartitions> logger)
{
    /// <summary>The partitioned table.</summary>
    public const string Table = "metric_point";

    /// <summary>The tables split into daily partitions: the points and their rollups.</summary>
    public static readonly IReadOnlyList<string> Tables = [Table, "metric_rollup_5m", "metric_rollup_1h"];

    /// <summary>Makes sure the partitions around the current time exist.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the partitions exist.</returns>
    public Task EnsureAsync(CancellationToken cancellationToken) => EnsureAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Makes sure the partitions around the given time exist.</summary>
    /// <param name="now">The time to plan around.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the partitions exist.</returns>
    public async Task EnsureAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        for (var offset = -1; offset <= 2; offset++)
        {
            var day = today.AddDays(offset);

            // The rollups (ADR 0015) get the same days as the points, so retention drops them together.
            foreach (var table in Tables)
            {
                if (!await DailyPartitions.EnsureDayAsync(dataSource, table, day, cancellationToken))
                {
                    LogDayStaysInDefault(table, day);
                }
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Partition of {Table} for {Day} not created: the default partition already holds rows for that day")]
    private partial void LogDayStaysInDefault(string table, DateOnly day);
}
