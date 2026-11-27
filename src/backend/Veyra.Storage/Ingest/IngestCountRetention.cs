using Microsoft.Extensions.Logging;
using Npgsql;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Ingest;

/// <summary>
/// Keeps the ingest counts (ADR 0023) for 30 days, the longest range a screen offers, by dropping whole days, and
/// deletes old rows from the default partition, which only a sender with a wrong clock fills.
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="clock">The clock that decides which days are old.</param>
/// <param name="logger">Receives a line for each day dropped.</param>
internal sealed partial class IngestCountRetention(
    NpgsqlDataSource dataSource,
    TimeProvider clock,
    ILogger<IngestCountRetention> logger)
{
    /// <summary>How many days the counts are kept: the longest range a screen offers.</summary>
    public const int Days = 30;

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
        var firstDayKept = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-Days);
        var dropped = new List<string>();
        var partitions = await DailyPartitions.ListAsync(dataSource, IngestCounts.Table, cancellationToken);

        foreach (var (name, _, _) in partitions.Where(partition => partition.Day < firstDayKept))
        {
            // The name comes from pg_class and was checked to be the table, an underscore, and a date.
            await using var drop = dataSource.CreateCommand($"DROP TABLE IF EXISTS storage.{name}");
            await drop.ExecuteNonQueryAsync(cancellationToken);
            dropped.Add(name);
            LogDropped(name, Days);
        }

        await using var command = dataSource.CreateCommand(
            $"DELETE FROM storage.{IngestCounts.Table}_default WHERE minute < $1");
        command.Parameters.AddWithValue(new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));

        return new RetentionResult(dropped, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention dropped ingest count partition {Partition}, older than {Days} days")]
    private partial void LogDropped(string partition, int days);
}
