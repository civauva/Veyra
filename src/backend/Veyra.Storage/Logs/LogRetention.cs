using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Veyra.Core.Telemetry;
using Veyra.Storage.Schema;

namespace Veyra.Storage.Logs;

/// <summary>
/// Removes old logs: by age, and by size when a storage budget is set. Also reports how much space logs use.
/// </summary>
/// <remarks>
/// <para>
/// Whole daily partitions are dropped, which is instant and frees disk at once, unlike deleting rows. A day's records
/// and its counts (ADR 0015) are dropped together. Only the default partitions, which catch records with unusual
/// timestamps, have their old rows deleted one by one.
/// </para>
/// <para>
/// The size budget is Veyra's disk guard. Veyra cannot see free space on the database host, so it keeps log storage
/// under a configured size instead: when over budget, it drops the oldest days first and logs a warning for each.
/// Today's partition is never dropped by the budget.
/// </para>
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="options">The Storage settings: retention days and the size budget.</param>
/// <param name="clock">The clock that decides what is old.</param>
/// <param name="logger">Receives what was removed.</param>
internal sealed partial class LogRetention(
    NpgsqlDataSource dataSource,
    IOptionsMonitor<StorageOptions> options,
    TimeProvider clock,
    ILogger<LogRetention> logger) : ILogStorageUsage
{
    // The tables whose daily partitions make up a day of logs: the records, then their counts.
    private static readonly string[] _tables = ["log_record", "log_count_minute", "log_count_hour"];

    private const double _bytesPerGigabyte = 1024d * 1024 * 1024;

    /// <summary>Applies retention and the size budget as of now.</summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What was removed.</returns>
    public Task<RetentionResult> ApplyAsync(CancellationToken cancellationToken) =>
        ApplyAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Applies retention and the size budget as of the given time.</summary>
    /// <param name="now">The time to measure age from.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What was removed.</returns>
    public async Task<RetentionResult> ApplyAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstDayKept = today.AddDays(-settings.LogRetentionDays);
        var dropped = new List<string>();

        // 1. By age: every daily partition before the first day kept.
        var partitions = await ListPartitionsAsync(cancellationToken);

        foreach (var partition in partitions.Where(p => p.Day < firstDayKept))
        {
            await DropAsync(partition, cancellationToken);
            dropped.Add(partition.Name);
            LogDroppedForAge(partition.Name, settings.LogRetentionDays);
        }

        var deleted = await DeleteOldDefaultRowsAsync(firstDayKept, cancellationToken);

        // 2. By size: oldest first, never today or later, until log storage fits the budget again.
        if (settings.MaxLogStorageGigabytes is { } budgetGigabytes)
        {
            var budget = (long)(budgetGigabytes * _bytesPerGigabyte);
            var remaining = partitions.Where(p => p.Day >= firstDayKept).OrderBy(p => p.Day).ToList();
            var total = remaining.Sum(p => p.Bytes) + await DefaultPartitionBytesAsync(cancellationToken);

            foreach (var partition in remaining.Where(p => p.Day < today))
            {
                if (total <= budget)
                {
                    break;
                }

                await DropAsync(partition, cancellationToken);
                dropped.Add(partition.Name);
                total -= partition.Bytes;
                LogDroppedForSize(partition.Name, budgetGigabytes);
            }
        }

        return new RetentionResult(dropped, deleted);
    }

    /// <inheritdoc />
    public Task<LogStorageUsage> GetUsageAsync(CancellationToken cancellationToken) =>
        GetUsageAsync(clock.GetUtcNow(), cancellationToken);

    /// <summary>Measures log storage as of the given time.</summary>
    /// <remarks>
    /// The daily average covers the last seven full days before today. Empty days at the start, which exist because
    /// partitions are created around today before any record arrives, are left out so a new installation does not
    /// report a misleadingly small average.
    /// </remarks>
    /// <param name="now">The time that decides which day is today.</param>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The size, the budget, and the recent daily growth.</returns>
    public async Task<LogStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var partitions = await ListPartitionsAsync(cancellationToken);
        var used = partitions.Sum(p => p.Bytes) + await DefaultPartitionBytesAsync(cancellationToken);

        var fullDays = partitions.Where(p => p.Day < today && p.Day >= today.AddDays(-7)).ToList();

        while (fullDays.Count > 0 && await IsEmptyAsync(fullDays[0].Name, cancellationToken))
        {
            fullDays.RemoveAt(0);
        }

        long? daily = fullDays.Count == 0 ? null : fullDays.Sum(p => p.Bytes) / fullDays.Count;
        long? budget = settings.MaxLogStorageGigabytes is { } gigabytes ? (long)(gigabytes * _bytesPerGigabyte) : null;

        return new LogStorageUsage(used, budget, settings.LogRetentionDays, daily);
    }

    private async Task<bool> IsEmptyAsync(string partition, CancellationToken cancellationToken)
    {
        // The name comes from pg_class and was checked to be log_record_ followed by a date, so it is safe to inline.
        await using var command = dataSource.CreateCommand($"SELECT NOT EXISTS (SELECT 1 FROM storage.{partition})");

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Lists the days of logs with their records' partition and the size of the records and counts.</summary>
    private async Task<List<Partition>> ListPartitionsAsync(CancellationToken cancellationToken)
    {
        var days = new Dictionary<DateOnly, (string? Name, long Bytes)>();

        foreach (var table in _tables)
        {
            foreach (var (name, day, bytes) in await DailyPartitions.ListAsync(dataSource, table, cancellationToken))
            {
                var (recordsName, sum) = days.GetValueOrDefault(day);
                days[day] = (table == _tables[0] ? name : recordsName, sum + bytes);
            }
        }

        // Records and counts get their partitions together, so a day always has a records partition.
        return days
            .Where(day => day.Value.Name is not null)
            .Select(day => new Partition(day.Value.Name!, day.Key, day.Value.Bytes))
            .OrderBy(partition => partition.Day)
            .ToList();
    }

    /// <summary>Drops a day: its records, its counts, and its places in the backfills.</summary>
    private async Task DropAsync(Partition partition, CancellationToken cancellationToken)
    {
        var day = partition.Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        // The names are built from a date this method formatted itself, so they are safe to inline.
        var tables = $"storage.log_record_{day}, storage.log_count_minute_{day}, storage.log_count_hour_{day}";

        await ExecuteAsync(
            cancellationToken,
            ($"DROP TABLE IF EXISTS {tables}", []),
            ("DELETE FROM storage.log_count_backfill WHERE day = $1", [partition.Day]),
            ("DELETE FROM storage.log_terms_backfill WHERE day = $1", [partition.Day]));
    }

    private async Task<long> DeleteOldDefaultRowsAsync(DateOnly firstDayKept, CancellationToken cancellationToken)
    {
        var before = new DateTimeOffset(firstDayKept.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        await ExecuteAsync(
            cancellationToken,
            ("DELETE FROM storage.log_count_minute_default WHERE minute < $1", [before]),
            ("DELETE FROM storage.log_count_hour_default WHERE hour < $1", [before]),
            ("DELETE FROM storage.log_count_backfill WHERE day < $1", [firstDayKept]),
            ("DELETE FROM storage.log_terms_backfill WHERE day < $1", [firstDayKept]));

        await using var command = dataSource.CreateCommand(
            "DELETE FROM storage.log_record_default WHERE timestamp < $1");
        command.Parameters.AddWithValue(before);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Runs several statements in one round trip.</summary>
    private async Task ExecuteAsync(
        CancellationToken cancellationToken,
        params (string Sql, object[] Values)[] statements)
    {
        await using var batch = dataSource.CreateBatch();

        foreach (var (sql, values) in statements)
        {
            var command = new NpgsqlBatchCommand(sql);

            foreach (var value in values)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value });
            }

            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<long> DefaultPartitionBytesAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT pg_total_relation_size('storage.log_record_default')
                + pg_total_relation_size('storage.log_count_minute_default')
                + pg_total_relation_size('storage.log_count_hour_default')
            """);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Retention dropped log partition {Partition}, older than {Days} days")]
    private partial void LogDroppedForAge(string partition, int days);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Disk guard dropped log partition {Partition}: log storage was over its {Budget} GB budget")]
    private partial void LogDroppedForSize(string partition, double budget);

    private sealed record Partition(string Name, DateOnly Day, long Bytes);
}
