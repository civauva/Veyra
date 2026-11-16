using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Traces;

/// <summary>
/// Creates the daily partitions of <c>storage.span</c> (ADR 0014) ahead of time.
/// </summary>
/// <remarks>
/// <para>
/// Partitions exist from yesterday to two days ahead, in UTC, so spans arriving around midnight or from a sender
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
internal sealed partial class SpanPartitions(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<SpanPartitions> logger)
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
        foreach (var table in new[] { SpanStore.Table })
        {
            if (!await DailyPartitions.EnsureDayAsync(dataSource, table, day, cancellationToken))
            {
                LogDayStaysInDefault(table, day);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Partition of {Table} for {Day} not created: the default partition already holds rows for that day")]
    private partial void LogDayStaysInDefault(string table, DateOnly day);
}
