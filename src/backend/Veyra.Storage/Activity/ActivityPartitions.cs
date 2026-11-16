using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Activity;

/// <summary>
/// Creates the daily partitions of the database activity tables (ADR 0020) ahead of time, from yesterday to two days
/// ahead in UTC, like those of logs and spans.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">The clock that decides which days are needed.</param>
/// <param name="logger">Receives a warning for each day that could not get its own partition.</param>
internal sealed partial class ActivityPartitions(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<ActivityPartitions> logger)
{
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

        foreach (var table in ActivityTables.All)
        {
            for (var offset = -1; offset <= 2; offset++)
            {
                var day = today.AddDays(offset);

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
