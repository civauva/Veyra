using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Traces;

/// <summary>
/// Keeps trace storage within its retention and its size budget (ADR 0014), by dropping whole days.
/// </summary>
/// <remarks>
/// <para>
/// Spans are the largest of the three signals, so they keep 7 days by default. Dropping a daily partition is instant
/// and returns its disk at once, unlike deleting rows. Spans in the default partition, which only a sender with a
/// wrong clock produces, are deleted by age instead.
/// </para>
/// <para>
/// With a budget set, the oldest days go first until trace storage fits again; today is always kept, because spans
/// of the last few minutes are what an incident needs.
/// </para>
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="options">The retention in days and the optional budget.</param>
/// <param name="clock">The clock that decides which days are old.</param>
/// <param name="logger">Receives a line for each day dropped.</param>
internal sealed partial class TraceRetention(
    NpgsqlDataSource dataSource,
    IOptionsMonitor<StorageOptions> options,
    TimeProvider clock,
    ILogger<TraceRetention> logger)
{
    private const double _bytesPerGigabyte = 1024d * 1024 * 1024;

    /// <summary>Applies retention and the budget now.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was dropped and deleted.</returns>
    public Task<RetentionResult> ApplyAsync(CancellationToken cancellationToken) =>
        ApplyAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Applies retention and the budget as of a given time.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was dropped and deleted.</returns>
    public async Task<RetentionResult> ApplyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstDayKept = today.AddDays(-settings.TraceRetentionDays);
        var partitions = await DailyPartitions.ListAsync(dataSource, SpanStore.Table, cancellationToken);
        var dropped = new List<string>();

        foreach (var (name, _, _) in partitions.Where(p => p.Day < firstDayKept))
        {
            await DropAsync(name, cancellationToken);
            dropped.Add(name);
            LogDroppedForAge(name, settings.TraceRetentionDays);
        }

        await using (var command = dataSource.CreateCommand("DELETE FROM storage.span_default WHERE start_time < $1"))
        {
            var before = new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            command.Parameters.AddWithValue(before);
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);

            if (settings.MaxTraceStorageGigabytes is not { } budgetGigabytes)
            {
                return new RetentionResult(dropped, deleted);
            }

            var budget = (long)(budgetGigabytes * _bytesPerGigabyte);
            var remaining = partitions.Where(p => p.Day >= firstDayKept).ToList();
            var total = remaining.Sum(p => p.Bytes) + await DefaultBytesAsync(cancellationToken);

            foreach (var (name, _, bytes) in remaining.Where(p => p.Day < today))
            {
                if (total <= budget)
                {
                    break;
                }

                await DropAsync(name, cancellationToken);
                dropped.Add(name);
                total -= bytes;
                LogDroppedForSize(name, budgetGigabytes);
            }

            return new RetentionResult(dropped, deleted);
        }
    }

    private async Task DropAsync(string partition, CancellationToken cancellationToken)
    {
        // The name comes from pg_class and was checked to be span_ followed by a date, so it is safe to inline.
        await using var command = dataSource.CreateCommand($"DROP TABLE IF EXISTS storage.{partition}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> DefaultBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT pg_total_relation_size('storage.span_default')");

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention dropped trace partition {Partition}, older than {Days} days")]
    private partial void LogDroppedForAge(string partition, int days);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Disk guard dropped trace partition {Partition}: trace storage was over its {Budget} GB budget")]
    private partial void LogDroppedForSize(string partition, double budget);
}
