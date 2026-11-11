using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Logs;

/// <summary>
/// Creates the daily partitions of <c>storage.log_record</c> ahead of time.
/// </summary>
/// <remarks>
/// <para>
/// Partitions exist from yesterday to two days ahead, in UTC, so records arriving around midnight or from a sender
/// whose clock is a little off find their partition. Everything else lands in the default partition and is kept.
/// </para>
/// <para>
/// PostgreSQL refuses to create a partition for a day that already has rows in the default partition. That only
/// happens after senders with a badly wrong clock; the day then stays in the default partition and a warning is logged.
/// </para>
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">The clock that decides which days are needed.</param>
/// <param name="logger">Receives a warning for each day that could not get its own partition.</param>
internal sealed partial class LogPartitions(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<LogPartitions> logger)
{
    /// <summary>
    /// Makes sure the partitions around the current time exist.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the partitions exist.</returns>
    public Task EnsureAsync(CancellationToken cancellationToken) => EnsureAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>
    /// Makes sure the partitions around the given time exist.
    /// </summary>
    /// <param name="now">The time to plan around.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes when the partitions exist.</returns>
    public async Task EnsureAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        for (var offset = -1; offset <= 2; offset++)
        {
            await EnsureDayAsync(today.AddDays(offset), cancellationToken);
        }
    }

    private async Task EnsureDayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        if (!await DailyPartitions.EnsureDayAsync(dataSource, "log_record", day, cancellationToken))
        {
            LogDayStaysInDefault(day);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Log partition for {Day} not created: the default partition already holds records for that day")]
    private partial void LogDayStaysInDefault(DateOnly day);
}
