using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Jobs;

/// <summary>
/// Creates the daily partitions of <c>storage.job_run</c> (ADR 0027) ahead of time, from yesterday to two days ahead
/// in UTC, like those of logs, metrics, and spans.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">The clock that decides which days are needed.</param>
/// <param name="logger">Receives a warning for each day that could not get its own partition.</param>
internal sealed partial class JobRunPartitions(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<JobRunPartitions> logger)
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

        for (var offset = -1; offset <= 2; offset++)
        {
            var day = today.AddDays(offset);

            if (!await DailyPartitions.EnsureDayAsync(dataSource, JobRuns.Table, day, cancellationToken))
            {
                LogDayStaysInDefault(day);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Job run partition for {Day} not created: the default partition already holds that day's rows")]
    private partial void LogDayStaysInDefault(DateOnly day);
}
