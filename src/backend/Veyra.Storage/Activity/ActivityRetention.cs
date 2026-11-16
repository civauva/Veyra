using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Activity;

/// <summary>
/// Keeps each database activity table (ADR 0020) within its retention by dropping whole days, and deletes old rows
/// from its default partition, which only an agent with a wrong clock fills.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="options">The retention of each table.</param>
/// <param name="clock">The clock that decides which days are old.</param>
/// <param name="logger">Receives a line for each day dropped.</param>
internal sealed partial class ActivityRetention(
    NpgsqlDataSource dataSource,
    IOptionsMonitor<StorageOptions> options,
    TimeProvider clock,
    ILogger<ActivityRetention> logger)
{
    /// <summary>Applies retention now.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was dropped and deleted.</returns>
    public Task<RetentionResult> ApplyAsync(CancellationToken cancellationToken) =>
        ApplyAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Applies retention as of a given time.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was dropped and deleted.</returns>
    public async Task<RetentionResult> ApplyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var dropped = new List<string>();
        long deleted = 0;

        foreach (var (table, days) in ActivityTables.Retention(options.CurrentValue))
        {
            var firstDayKept = today.AddDays(-days);

            foreach (var (name, _, _) in (await DailyPartitions.ListAsync(dataSource, table, cancellationToken))
                .Where(partition => partition.Day < firstDayKept))
            {
                // The name comes from pg_class and was checked to be the table, an underscore, and a date.
                await using var drop = dataSource.CreateCommand($"DROP TABLE IF EXISTS storage.{name}");
                await drop.ExecuteNonQueryAsync(cancellationToken);
                dropped.Add(name);
                LogDropped(name, days);
            }

            // The table name is one of ActivityTables' constants, never input.
            await using var command = dataSource.CreateCommand(
                $"DELETE FROM storage.{table}_default WHERE sampled_at < $1");
            command.Parameters.AddWithValue(
                new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new RetentionResult(dropped, deleted);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention dropped database activity partition {Partition}, older than {Days} days")]
    private partial void LogDropped(string partition, int days);
}
