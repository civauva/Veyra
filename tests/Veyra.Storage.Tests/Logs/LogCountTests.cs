using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Logs;

/// <summary>
/// The counts kept as records are written (ADR 0015) must always give exactly the numbers of the records.
/// </summary>
[Collection(PostgresGroup.Name)]
public class LogCountTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_TwoBatches_AddUpTheCountsPerMinuteAndHour()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var at = _day.AddHours(10).AddMinutes(5);

        await store.WriteAsync(
            [
                Record(at.AddSeconds(1), LogSeverity.Information),
                Record(at.AddSeconds(59), LogSeverity.Information),
                Record(at.AddSeconds(30), LogSeverity.Error),
                Record(at.AddMinutes(1), LogSeverity.Information) with { Environment = null },
            ],
            default);
        await store.WriteAsync([Record(at.AddSeconds(2), LogSeverity.Information)], default);

        Assert.Equal(
            [
                $"{at:HH:mm} production payments {(short)LogSeverity.Information} 3",
                $"{at:HH:mm} production payments {(short)LogSeverity.Error} 1",
                $"{at.AddMinutes(1):HH:mm}  payments {(short)LogSeverity.Information} 1",
            ],
            await CountRowsAsync(database, "log_count_minute", "minute"));
        Assert.Equal(
            [
                $"10:00 production payments {(short)LogSeverity.Information} 3",
                $"10:00 production payments {(short)LogSeverity.Error} 1",
                $"10:00  payments {(short)LogSeverity.Information} 1",
            ],
            await CountRowsAsync(database, "log_count_hour", "hour"));
    }

    [Fact]
    public async Task WriteAsync_DatabaseEvents_AreCountedButNeverAService()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var at = _day.AddHours(10).AddMinutes(5);

        await store.WriteAsync(
            [
                Record(at, LogSeverity.Error),
                Record(at, LogSeverity.Information) with
                {
                    ServiceName = "microsoft.sql_server sql-prod-01",
                    BlockKind = AlertBlockKind.Database,
                    HostName = "db-host-01",
                },
            ],
            default);
        var window = new LogQuery(at.AddMinutes(-5), at.AddMinutes(5), null, null, null, null, 100, null);

        var rates = await store.GetServiceRatesAsync(window.From, window.To, default);
        var summary = await store.SummarizeAsync(window, TimeSpan.FromMinutes(10), default);

        Assert.Equal("payments", Assert.Single(rates).ServiceName);
        Assert.Equal(2, summary.Buckets.Sum(bucket => bucket.Total));
        Assert.Equal("payments", Assert.Single(summary.Services).Name);
        Assert.Equal(
            [
                $"{at:HH:mm} production microsoft.sql_server sql-prod-01 {(short)LogSeverity.Information} 1",
                $"{at:HH:mm} production payments {(short)LogSeverity.Error} 1",
            ],
            (await CountRowsAsync(database, "log_count_minute", "minute")).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(10 * 3600 + 207, 13 * 3600 + 2470, 3600, null, null, null)]
    [InlineData(10 * 3600 + 207, 13 * 3600 + 2470, 300, null, null, null)]
    [InlineData(10 * 3600 + 207, 13 * 3600 + 2470, 30, null, null, null)]
    [InlineData(9 * 3600, 15 * 3600, 3600, LogSeverity.Error, null, null)]
    [InlineData(10 * 3600 + 59, 12 * 3600 + 1, 3600, null, "orders", null)]
    [InlineData(10 * 3600 + 1, 14 * 3600 + 7, 900, LogSeverity.Warning, "payments", "staging")]
    [InlineData(0, 86400, 86400, null, null, "production")]
    public async Task SummarizeAsync_AnyWindowBucketAndFilter_MatchesTheRecordsExactly(
        int fromSecond,
        int toSecond,
        int bucketSeconds,
        LogSeverity? severity,
        string? service,
        string? environment)
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var records = Spread();
        await store.WriteAsync(records, default);
        var query = new LogQuery(
            _day.AddSeconds(fromSecond),
            _day.AddSeconds(toSecond),
            severity,
            service is null ? null : [service],
            null,
            null,
            100,
            null,
            environment is null ? null : [environment]);
        var bucketSize = TimeSpan.FromSeconds(bucketSeconds);

        var summary = await store.SummarizeAsync(query, bucketSize, default);

        Assert.Equal(Expected(records, query, bucketSize), summary.Buckets);
        Assert.Equal(ExpectedServices(records, query), summary.Services);
    }

    [Fact]
    public async Task WidenScanAsync_InATransaction_GivesMoreMemoryAndWorkersUntilItEnds()
    {
        await using var database = await CreateAsync();
        await using var connection = await database.OpenConnectionAsync();
        var before = await SettingAsync(connection, null, "work_mem");

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await LogCounts.WidenScanAsync(connection, transaction, default);

            Assert.Equal("64MB", await SettingAsync(connection, transaction, "work_mem"));
            Assert.Equal("4", await SettingAsync(connection, transaction, "max_parallel_workers_per_gather"));
            Assert.Equal("0", await SettingAsync(connection, transaction, "parallel_setup_cost"));
            Assert.Equal("0.01", await SettingAsync(connection, transaction, "parallel_tuple_cost"));

            await transaction.CommitAsync();
        }

        Assert.Equal(before, await SettingAsync(connection, null, "work_mem"));
    }

    [Fact]
    public async Task SummarizeAsync_WordFilter_CountsTheMatchingRecords()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var records = Spread();
        await store.WriteAsync(records, default);
        var query = new LogQuery(_day.AddHours(10), _day.AddHours(13), null, null, "declined", null, 100, null);

        var summary = await store.SummarizeAsync(query, TimeSpan.FromHours(1), default);

        Assert.Equal(Expected(records, query, TimeSpan.FromHours(1)), summary.Buckets);
        Assert.Equal(ExpectedServices(records, query), summary.Services);
    }

    [Fact]
    public async Task GetServiceRatesAsync_UnalignedWindow_MatchesTheRecordsExactly()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var records = Spread();
        await store.WriteAsync(records, default);
        var from = _day.AddHours(10).AddMinutes(57).AddSeconds(13);
        var to = _day.AddHours(12).AddMinutes(2).AddSeconds(44);

        var rates = await store.GetServiceRatesAsync(from, to, default);

        var expected = records
            .Where(record => record.Timestamp >= from && record.Timestamp < to)
            .GroupBy(record => (record.Environment, record.ServiceName))
            .Select(group => new ServiceLogRate(
                group.Key.Environment,
                group.Key.ServiceName,
                group.Count(),
                group.Count(record => record.Severity >= LogSeverity.Error)))
            .OrderBy(rate => rate.Environment)
            .ThenBy(rate => rate.ServiceName);
        Assert.Equal(expected, rates.OrderBy(rate => rate.Environment).ThenBy(rate => rate.ServiceName));
    }

    [Fact]
    public async Task SummarizeAsync_DayNotCountedYet_CountsItsRecords()
    {
        await using var database = await CreateAsync();
        var store = new LogStore(database);
        var records = Spread();
        await store.WriteAsync(records, default);

        // As after an upgrade: the day's records exist but its counts do not, and the backfill has the day to do.
        await ExecuteAsync(database, "DELETE FROM storage.log_count_minute; DELETE FROM storage.log_count_hour");
        await ExecuteAsync(database, "INSERT INTO storage.log_count_backfill (day, up_to_id) VALUES ('2026-11-09', 0)");
        var query = new LogQuery(_day, _day.AddDays(1), null, null, null, null, 100, null);

        var summary = await store.SummarizeAsync(query, TimeSpan.FromHours(1), default);

        Assert.Equal(Expected(records, query, TimeSpan.FromHours(1)), summary.Buckets);
    }

    [Fact]
    public async Task EnsurePartitionsAsync_Today_StoresCountsInTheDailyPartitions()
    {
        await using var database = await CreateAsync();
        await new LogPartitions(database, TimeProvider.System, NullLogger<LogPartitions>.Instance)
            .EnsureAsync(_day.AddHours(12), default);

        await new LogStore(database).WriteAsync([Record(_day.AddHours(12), LogSeverity.Information)], default);

        Assert.Equal("storage.log_count_minute_20261109", await TableOfAsync(database, "log_count_minute"));
        Assert.Equal("storage.log_count_hour_20261109", await TableOfAsync(database, "log_count_hour"));
    }

    /// <summary>Records over the whole day at uneven times, from two services, three environments, four severities.</summary>
    private static List<LogRecord> Spread()
    {
        var severities = new[] { LogSeverity.Debug, LogSeverity.Information, LogSeverity.Warning, LogSeverity.Error };
        var environments = new[] { "production", "staging", null };
        var records = new List<LogRecord>();

        for (var i = 0; i < 600; i++)
        {
            var at = _day.AddMilliseconds(i * 137_300L % 86_400_000);

            records.Add(Record(at, severities[i % severities.Length]) with
            {
                ServiceName = i % 3 == 0 ? "orders" : "payments",
                Environment = environments[i % environments.Length],
                Body = i % 5 == 0 ? "payment declined" : "payment accepted",
            });
        }

        // Records exactly on minute and hour boundaries, where an off-by-one would show.
        records.Add(Record(_day.AddHours(11), LogSeverity.Error));
        records.Add(Record(_day.AddHours(12).AddMinutes(1), LogSeverity.Information));

        return records;
    }

    private static List<LogBucket> Expected(List<LogRecord> records, LogQuery query, TimeSpan bucketSize)
    {
        var matching = Matching(records, query).Where(record => query.Services is null
            || query.Services.Contains(record.ServiceName)).ToList();

        return LogCountPlan.BucketStarts(query.From, query.To, bucketSize)
            .Select((start, index) =>
            {
                var inside = matching
                    .Where(record => LogCountPlan.BucketOf(record.Timestamp, query.From, bucketSize) == index)
                    .ToList();

                return new LogBucket(start, inside.Count, inside.Count(record => record.Severity >= LogSeverity.Error));
            })
            .ToList();
    }

    private static List<ServiceCount> ExpectedServices(List<LogRecord> records, LogQuery query) =>
        Matching(records, query)
            .GroupBy(record => record.ServiceName)
            .Select(group => new ServiceCount(group.Key, group.Count()))
            .OrderByDescending(count => count.Count)
            .ThenBy(count => count.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The records that match every filter but the service, as the service counts need.</summary>
    private static IEnumerable<LogRecord> Matching(List<LogRecord> records, LogQuery query) =>
        records.Where(record => record.Timestamp >= query.From
            && record.Timestamp < query.To
            && (query.MinimumSeverity is not { } severity || record.Severity >= severity)
            && (query.Environments is null || query.Environments.Contains(record.Environment))
            && (query.Text is null || record.Body.Contains(query.Text, StringComparison.OrdinalIgnoreCase)));

    private static LogRecord Record(DateTimeOffset timestamp, LogSeverity severity) => new(
        Timestamp: timestamp,
        ObservedTimestamp: timestamp,
        Severity: severity,
        SeverityText: severity.ToString(),
        Body: "payment accepted",
        ServiceName: "payments",
        Environment: "production",
        TraceId: null,
        SpanId: null,
        Attributes: new Dictionary<string, string>(),
        ResourceAttributes: new Dictionary<string, string>());

    private Task<NpgsqlDataSource> CreateAsync() => postgres.CreateMigratedDatabaseAsync(new StorageMigrations());

    private static async Task<List<string>> CountRowsAsync(NpgsqlDataSource database, string table, string time)
    {
        // The table and column are constants of this test.
        await using var command = database.CreateCommand(
            $"""
            SELECT to_char({time} AT TIME ZONE 'UTC', 'HH24:MI') || ' ' || coalesce(environment, '') || ' '
                || service_name || ' ' || severity || ' ' || records
            FROM storage.{table}
            ORDER BY {time}, environment NULLS LAST, severity
            """);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string>();

        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecuteAsync(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> TableOfAsync(NpgsqlDataSource database, string table)
    {
        await using var command = database.CreateCommand($"SELECT tableoid::regclass::text FROM storage.{table}");

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> SettingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string name)
    {
        await using var command = new NpgsqlCommand("SELECT current_setting($1)", connection, transaction);
        command.Parameters.AddWithValue(name);

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
